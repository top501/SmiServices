﻿
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microservices.Common.Events;
using Microservices.Common.Execution;
using Microservices.Common.Messages;
using Microservices.Common.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Microservices.Common.Messaging
{
    public class ControlMessageConsumer : Consumer
    {
        public readonly ConsumerOptions ControlConsumerOptions = new ConsumerOptions
        {
            QoSPrefetchCount = 1,
            AutoAck = true
        };

        public event StopEventHandler StopHost;
        public event ControlEventHandler ControlEvent;


        private readonly string _processName;
        private readonly string _processId;

        private readonly ConnectionFactory _factory;

        private const string ControlQueueBindingKey = "smi.control.all.*";


        public ControlMessageConsumer(MicroserviceHost host, RabbitOptions options, string processName, int processId)
        {
            _processName = processName.ToLower();
            _processId = processId.ToString();

            ControlConsumerOptions.QueueName = "Control." + _processName + _processId;

            _factory = new ConnectionFactory
            {
                HostName = options.RabbitMqHostName,
                VirtualHost = options.RabbitMqVirtualHost,
                Port = options.RabbitMqHostPort,
                UserName = options.RabbitMqUserName,
                Password = options.RabbitMqPassword
            };

            SetupControlQueueForHost(options);

            StopHost += () => host.Stop("Control message stop");
        }


        /// <summary>
        /// Recreate ProcessMessage to specifically handle control messages which wont have headers,
        /// and shouldn't be included in any Ack/Nack counts
        /// </summary>
        /// <param name="model"></param>
        /// <param name="e"></param>
        public override void ProcessMessage(BasicDeliverEventArgs e)
        {
            Logger.Info("Control message received");

            try
            {
                ProcessMessageImpl(null, e);
            }
            catch (Exception exception)
            {
                Fatal("ProcessMessageImpl threw unhandled exception", exception);
            }
        }

        /// <summary>
        /// Ensures the control queue is cleaned up on exit. Should have been deleted already, but this ensures it
        /// </summary>
        public void Shutdown()
        {
            using (IConnection connection = _factory.CreateConnection(_processName + _processId + "-ControlQueueShutdown"))
            using (IModel model = connection.CreateModel())
            {
                Logger.Debug("Deleting control queue: " + ControlConsumerOptions.QueueName);
                model.QueueDelete(ControlConsumerOptions.QueueName);
            }
        }

        protected override void ProcessMessageImpl(IMessageHeader header, BasicDeliverEventArgs e)
        {
            // For now we only deal with the simple case of "smi.control.<who>.<what>". Can expand later on depending on our needs
            // Queues will be deleted when the connection is closed so don't need to worry about messages being leftover

            Logger.Info("Control message received with routing key: " + e.RoutingKey);

            string[] split = e.RoutingKey.ToLower().Split('.');
            string body = GetBodyFromArgs(e);

            if (split.Length < 4)
            {
                Logger.Debug("Control command shorter than the minimum format");
                return;
            }

            // Who, what
            string actor = string.Join(".", split.Skip(2).Take(split.Length - 3));
            string action = split[split.Length - 1];

            // Shouldn't get any messages not meant for us, but good to check
            if (!actor.Equals("all") && !actor.Equals(_processName))
            {
                Logger.Debug("Control command did not match this service");
                return;
            }

            // Try and handle any general actions we want for any microservice - just stop and ping for now

            if (action.Equals("stop") || (action.StartsWith("stop") && action.Substring(4).Equals(_processId)))
            {
                if (StopHost == null)
                {
                    // This should never really happen
                    Logger.Info("Received stop command but no stop event registered");
                    return;
                }

                Logger.Info("Stop request received, raising StopHost event");
                Task.Run(() => StopHost.Invoke());

                return;
            }

            if (action.Equals("ping") || (action.StartsWith("ping") && action.Substring(4).Equals(_processId)))
            {
                Logger.Info("Pong!");
                return;
            }

            // Don't pass any unhandled broadcast (to "all") messages down to the hosts
            if (actor.Equals("all"))
                return;

            // Else raise the event if any hosts have specific control needs
            if (ControlEvent != null)
            {
                Logger.Debug("Control message not handled, raising registered ControlEvent(s)");
                ControlEvent(Regex.Replace(action, @"[\d]", ""), body);

                return;
            }

            // Else we should ignore it?
            Logger.Warn("Unhandled control message with routing key: " + e.RoutingKey);
        }

        protected override void ErrorAndNack(IMessageHeader header, BasicDeliverEventArgs deliverEventArgs, string message, Exception exception)
        {
            // Can't Nack the message since it is automatically acknowledged!
            // This shouldn't really be called for control messages
            throw new Exception("ErrorAndNack called for control message with routing key: " + deliverEventArgs.RoutingKey);
        }

        /// <summary>
        /// Creates a one-time connection to set up the required control queue and bindings on the RabbitMQ server.
        /// The connection is disposed and StartConsumer(...) can then be called on the parent RabbitMQAdapter with ControlConsumerOptions
        /// </summary>
        /// <param name="options"></param>
        private void SetupControlQueueForHost(RabbitOptions options)
        {
            using (IConnection connection = _factory.CreateConnection(_processName + _processId + "-ControlQueueSetup"))
            using (IModel model = connection.CreateModel())
            {
                try
                {
                    model.ExchangeDeclarePassive(options.RabbitMqControlExchangeName);
                }
                catch (OperationInterruptedException e)
                {
                    throw new ApplicationException("The given control exchange was not found on the server: \"" + options.RabbitMqControlExchangeName + "\"", e);
                }

                Logger.Debug("Creating control queue " + ControlConsumerOptions.QueueName);

                // Declare our queue with:
                // durable = false (queue will not persist over restarts of the RabbitMq server)
                // exclusive = false (queue won't be deleted when THIS connection closes)
                // autoDelete = true (queue will be deleted after a consumer connects and then disconnects)
                model.QueueDeclare(ControlConsumerOptions.QueueName, durable: false, exclusive: false, autoDelete: true);

                Logger.Debug("Creating binding " + options.RabbitMqControlExchangeName + "->" + ControlConsumerOptions.QueueName + " with key " + ControlQueueBindingKey);

                // Binding for any control requests, i.e. "stop"
                model.QueueBind(ControlConsumerOptions.QueueName, options.RabbitMqControlExchangeName, ControlQueueBindingKey);

                // Specific microservice binding key, ignoring the id at the end of the process name
                string bindingKey = "smi.control." + _processName + ".*";

                Logger.Debug("Creating binding " + options.RabbitMqControlExchangeName + "->" + ControlConsumerOptions.QueueName + " with key " + bindingKey);

                model.QueueBind(ControlConsumerOptions.QueueName, options.RabbitMqControlExchangeName, bindingKey);
            }
        }

        private static string GetBodyFromArgs(BasicDeliverEventArgs e)
        {
            if (e.Body == null || e.Body.Length == 0)
                return null;

            Encoding enc = null;

            if (!string.IsNullOrWhiteSpace(e.BasicProperties.ContentEncoding))
            {
                try
                {
                    enc = Encoding.GetEncoding(e.BasicProperties.ContentEncoding);
                }
                catch (ArgumentException)
                {
                    /* Ignored */
                }
            }

            if (enc == null)
                enc = Encoding.UTF8;

            return enc.GetString(e.Body);
        }
    }
}