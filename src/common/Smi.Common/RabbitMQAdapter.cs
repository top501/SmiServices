
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Smi.Common.Events;
using Smi.Common.Messaging;
using Smi.Common.Options;

namespace Smi.Common
{
    /// <summary>
    /// Adapter for the RabbitMQ API.
    /// </summary>
    public class RabbitMqAdapter : IRabbitMqAdapter
    {
        /// <summary>
        /// Used to ensure we can't create any new connections after we have called Shutdown()
        /// </summary>
        public bool ShutdownCalled { get; private set; }

        public bool HasConsumers
        {
            get
            {
                return _rabbitResources.Any(x => x.Value as ConsumerResources != null);
            }
        }

        public const string RabbitMqRoutingKey_MatchAnything = "#";
        public const string RabbitMqRoutingKey_MatchOneWord = "*";

        public static TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(5);

        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly HostFatalHandler _hostFatalHandler;
        private readonly string _hostId;

        private readonly IConnectionFactory _factory;
        private readonly ConcurrentDictionary<Guid, RabbitResources> _rabbitResources = new ConcurrentDictionary<Guid, RabbitResources>();
        
        private const int MinRabbitServerVersionMajor = 3;
        private const int MinRabbitServerVersionMinor = 7;
        private const int MinRabbitServerVersionPatch = 0;

        private const int MaxSubscriptionAttempts = 5;

        private readonly bool _threaded;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="connectionFactory"></param>
        /// <param name="hostId">Identifier for this host instance</param>
        /// <param name="hostFatalHandler"></param>
        /// <param name="threaded"></param>
        public RabbitMqAdapter(IConnectionFactory connectionFactory, string hostId, HostFatalHandler hostFatalHandler = null, bool threaded = false)
        {
            //_threaded = options.ThreadReceivers;
            _threaded = threaded;

            if (_threaded)
            {
                int minWorker, minIOC;
                ThreadPool.GetMinThreads(out minWorker, out minIOC);
                int workers = Math.Max(50, minWorker);
                if (ThreadPool.SetMaxThreads(workers, 50))
                    _logger.Info($"Set Rabbit event concurrency to ({workers},50)");
                else
                    _logger.Warn($"Failed to set Rabbit event concurrency to ({workers},50)");
            }

            _factory = connectionFactory;
            
            if (string.IsNullOrWhiteSpace(hostId))
                throw new ArgumentException("hostId");

            _hostId = hostId;

            if (hostFatalHandler == null)
                _logger.Warn("No handler given for fatal events");

            _hostFatalHandler = hostFatalHandler;

            CheckValidServerSettings();
        }


        /// <summary>
        /// Setup a subscription to a queue which sends messages to the <see cref="IConsumer"/>.
        /// </summary>
        /// <param name="consumerOptions">The connection options.</param>
        /// <param name="consumer">Consumer that will be sent any received messages.</param>
        /// <param name="isSolo">If specified, will ensure that it is the only consumer on the provided queue</param>
        /// <returns>Identifier for the consumer task, can be used to stop the consumer without shutting down the whole adapter</returns>
        public Guid StartConsumer(ConsumerOptions consumerOptions, IConsumer consumer, bool isSolo = false)
        {
            if (ShutdownCalled)
                throw new ApplicationException("Adapter has been shut down");

            if (consumerOptions == null)
                throw new ArgumentNullException(nameof(consumerOptions));

            if (!consumerOptions.VerifyPopulated())
                throw new ArgumentException("The given ConsumerOptions has invalid values");

            // Client label is the same for the IConnection and Subscription since we have a separate connection per consumer
            string label = string.Format("{0}::Consumer::{1}", _hostId, consumerOptions.QueueName);

            IConnection connection = _factory.CreateConnection(label);

            connection.ConnectionBlocked += (s, a) => _logger.Warn($"ConnectionBlocked for {consumerOptions.QueueName} ( Reason: {a.Reason})");
            connection.ConnectionUnblocked += (s, a) => _logger.Warn($"ConnectionUnblocked for {consumerOptions.QueueName}");

            IModel model = connection.CreateModel();
            model.BasicQos(0, consumerOptions.QoSPrefetchCount, false);

            // Check queue exists

            try
            {
                // Passively declare the queue (equivalent to checking the queue exists)
                model.QueueDeclarePassive(consumerOptions.QueueName);
            }
            catch (OperationInterruptedException e)
            {
                model.Close(200, "StartConsumer - Queue missing");
                connection.Close(200, "StartConsumer - Queue missing");

                throw new ApplicationException($"Expected queue \"{consumerOptions.QueueName}\" to exist", e);
            }

            if (isSolo && model.ConsumerCount(consumerOptions.QueueName) > 0)
            {
                model.Close(200, "StartConsumer - Already a consumer on the queue");
                connection.Close(200, "StartConsumer - Already a consumer on the queue");

                throw new ApplicationException($"Already a consumer on queue {consumerOptions.QueueName} and solo consumer was specified");
            }

            EventingBasicConsumer _consumer = new EventingBasicConsumer(model);
            ReaderWriterLockSlim worklock = new ReaderWriterLockSlim();
            var taskTokenSource = new CancellationTokenSource();
            _consumer.Received += (model, ea) => {
                if (_threaded)
                {
                    Task.Run(() =>
                    {
                        worklock.EnterReadLock();
                        try
                        {
                            consumer.ProcessMessage(ea);
                        }
                        finally
                        {
                            worklock.ExitReadLock();
                        }
                    });
                }
                else
                    consumer.ProcessMessage(ea);
            };
            _consumer.Shutdown +=(sender,ea)=>{
                if (_threaded)
                {
                    // Taking a write lock means waiting for all read locks to
                    // release, i.e. all workers have finished
                    worklock.EnterWriteLock();

                    // Now there are no *new* messages being processed, flush the queue
                    consumer.Shutdown();
                    worklock.ExitWriteLock();
                }
                worklock.Dispose();
                string reason = "shutdown was called";
                if (model.IsClosed)
                    reason += ", channel is closed";
                if (taskTokenSource.IsCancellationRequested)
                    reason += ", cancellation was requested";
                _logger.Debug("Consumer for {0} exiting ({1})", consumerOptions.QueueName, reason);
            };

            Guid taskId = Guid.NewGuid();

            var consumerTask = new Task(() => { model.BasicConsume(consumerOptions.QueueName, consumerOptions.AutoAck, _consumer); });

            var resources = new ConsumerResources
            {
                Connection = connection,
                Model = model,
                ConsumerTask = consumerTask,
                TokenSource = taskTokenSource
            };

            Debug.Assert(_rabbitResources.TryAdd(taskId, resources));

            consumer.OnFatal += (s, e) =>
            {
                resources.Shutdown(DefaultOperationTimeout);
                _hostFatalHandler(s, e);
            };

            consumerTask.Start();
            _logger.Debug($"Consumer task started [QueueName={consumerOptions.QueueName}]");

            return taskId;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="taskId"></param>
        /// <param name="timeout"></param>
        public void StopConsumer(Guid taskId, TimeSpan timeout)
        {
            if (ShutdownCalled)
                return;

            RabbitResources res;
            if (!_rabbitResources.TryRemove(taskId, out res))
                throw new ApplicationException("Guid was not found in the task register");

            if (res is ConsumerResources)
            {
                var cr = (ConsumerResources)res;
                if (!cr.Shutdown(timeout))
                    throw new ApplicationException($"Consume task did not exit in time: {cr.QueueName}");
            }
        }

        /// <summary>
        /// Setup a <see cref="IProducerModel"/> to send messages with.
        /// </summary>
        /// <param name="producerOptions">The producer options class to setup which must include the exchange name.</param>
        /// <param name="isBatch"></param>
        /// <returns>Object which can send messages to a RabbitMQ exchange.</returns>
        public IProducerModel SetupProducer(ProducerOptions producerOptions, bool isBatch = false)
        {
            if (ShutdownCalled)
                throw new ApplicationException("Adapter has been shut down");

            if (!producerOptions.VerifyPopulated())
                throw new ArgumentException("The given producer options have invalid values");

            //TODO Should maybe refactor this so we have 1 IConnection per service
            //NOTE: IConnection objects are thread safe
            IConnection connection = _factory.CreateConnection(string.Format("{0}::Producer::{1}", _hostId, producerOptions.ExchangeName));

            //NOTE: IModel objects are /not/ thread safe
            IModel model = connection.CreateModel();
            model.ConfirmSelect();

            try
            {
                // Passively declare the exchange (equivalent to checking the exchange exists)
                model.ExchangeDeclarePassive(producerOptions.ExchangeName);
            }
            catch (OperationInterruptedException e)
            {
                model.Close(200, "SetupProducer - Exchange missing");
                connection.Close(200, "SetupProducer - Exchange missing");

                throw new ApplicationException($"Expected exchange \"{producerOptions.ExchangeName}\" to exist", e);
            }

            IBasicProperties props = model.CreateBasicProperties();
            props.ContentEncoding = "UTF-8";
            props.ContentType = "application/json";
            props.Persistent = true;

            IProducerModel producerModel;

            try
            {
                producerModel = isBatch ?
                    new BatchProducerModel(producerOptions.ExchangeName, model, props, producerOptions.MaxConfirmAttempts) :
                    new ProducerModel(producerOptions.ExchangeName, model, props, producerOptions.MaxConfirmAttempts);
            }
            catch (Exception)
            {
                model.Close(200, "SetupProducer - Couldn't create ProducerModel");
                connection.Close(200, "SetupProducer - Couldn't create ProducerModel");

                throw;
            }

            var resources = new ProducerResources
            {
                Connection = connection,
                Model = model,
                ProducerModel = producerModel,
            };

            Debug.Assert(_rabbitResources.TryAdd(Guid.NewGuid(), resources));

            producerModel.OnFatal += (s, ra) =>
            {
                resources.Dispose();
                _hostFatalHandler.Invoke(s, new FatalErrorEventArgs(ra));
            };

            return producerModel;
        }

        /// <summary>
        /// Get a blank model with no options set
        /// </summary>
        /// <param name="connectionName"></param>
        /// <returns></returns>
        public IModel GetModel(string connectionName)
        {
            //TODO This method has no callback available for fatal errors

            if (ShutdownCalled)
                throw new ApplicationException("Adapter has been shut down");

            IConnection connection = _factory.CreateConnection(connectionName);
            IModel model = connection.CreateModel();

            Debug.Assert(_rabbitResources.TryAdd(Guid.NewGuid(), new RabbitResources
            {
                Connection = connection,
                Model = model
            }));

            return model;
        }

        /// <summary>
        /// Close all open connections and stop any consumers
        /// </summary>
        /// <param name="timeout">Max time given for each consumer to exit</param>
        public void Shutdown(TimeSpan timeout)
        {
            if (ShutdownCalled)
                return;

            ShutdownCalled = true;

            var exitOk = true;
            var failedToExit = new List<string>();

            foreach (RabbitResources res in _rabbitResources.Values)
            {
                var asConsumerRes = res as ConsumerResources;

                if (asConsumerRes != null)
                {
                    exitOk &= asConsumerRes.Shutdown(timeout);
                    failedToExit.Add(asConsumerRes.QueueName);
                }
                else
                {
                    res.Dispose();
                }
            }

            if (!exitOk)
                throw new ApplicationException($"Some consumer tasks did not exit in time: {string.Join(", ", failedToExit)}");

            _rabbitResources.Clear();
        }

        /// <summary>
        /// Checks we can create a basic connection, and also that the minimum RabbitMQ server version is met
        /// </summary>
        private void CheckValidServerSettings()
        {
            try
            {
                using (IConnection connection = _factory.CreateConnection("ServerDiscovery"))
                {
                    if (!connection.ServerProperties.ContainsKey("version"))
                        throw new ApplicationException("Could not get RabbitMQ server version");

                    string version = Encoding.UTF8.GetString((byte[])connection.ServerProperties["version"]);
                    string[] split = version.Split('.');

                    if (int.Parse(split[0]) < MinRabbitServerVersionMajor ||
                        int.Parse(split[1]) < MinRabbitServerVersionMinor ||
                        int.Parse(split[2]) < MinRabbitServerVersionPatch)
                    {

                        throw new ApplicationException(string.Format("Connected to RabbitMQ server version {0}, but minimum required is {1}.{2}.{3}",
                        version, MinRabbitServerVersionMajor, MinRabbitServerVersionMinor, MinRabbitServerVersionPatch));
                    }

                    _logger.Debug($"Connected to RabbitMQ server version {version}");
                }
            }
            catch (BrokerUnreachableException e)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"    URI:                            {_factory.Uri}");
                sb.AppendLine($"    UserName:                       {_factory.UserName}");
                sb.AppendLine($"    VirtualHost:                    {_factory.VirtualHost}");
                sb.AppendLine($"    HandshakeContinuationTimeout:   {_factory.HandshakeContinuationTimeout}");
                throw new ArgumentException($"Could not create a connection to RabbitMQ on startup:{Environment.NewLine}{sb}", e);
            }
        }

        #region Resource Classes

        private class RabbitResources : IDisposable
        {
            public IConnection Connection { get; set; }

            public IModel Model { get; set; }


            protected readonly object OResourceLock = new object();

            protected readonly ILogger Logger;

            public RabbitResources()
            {
                Logger = LogManager.GetLogger(GetType().Name);
            }


            public void Dispose()
            {
                lock (OResourceLock)
                {
                    if (Model.IsOpen)
                        Model.Close(200, "Disposed");
                    
                    if (Connection.IsOpen)
                        Connection.Close(200, "Disposed", System.TimeSpan.FromSeconds(10));
                }
            }
        }

        private class ProducerResources : RabbitResources
        {
            public IProducerModel ProducerModel { get; set; }
        }

        private class ConsumerResources : RabbitResources
        {
            public Task ConsumerTask { get; set; }

            public CancellationTokenSource TokenSource { get; set; }
            public string QueueName { get; private set; }

            public bool Shutdown(TimeSpan timeout)
            {
                bool exitOk;
                lock (OResourceLock)
                {
                    TokenSource.Cancel();

                    // Consumer task can't directly shut itself down, as it will block here
                    exitOk = ConsumerTask.Wait(timeout);

                    Dispose();
                }

                Logger.Debug($"Consumer task shutdown [QueueName={QueueName}]");

                return exitOk;
            }
        }

        #endregion
    }
}
