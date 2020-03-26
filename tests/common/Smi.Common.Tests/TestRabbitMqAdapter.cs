using System;
using RabbitMQ.Client;
using Smi.Common.Messaging;
using Smi.Common.Options;

namespace Smi.Common.Tests
{
    public class TestRabbitMqAdapter : IRabbitMqAdapter
    {
        public bool HasConsumers { get; }

        public Guid StartConsumer(ConsumerOptions consumerOptions, IConsumer consumer, bool isSolo)
        {
            throw new NotImplementedException();
        }

        public void StopConsumer(Guid taskId, TimeSpan timeout)
        {
            throw new NotImplementedException();
        }

        public IProducerModel SetupProducer(ProducerOptions producerOptions, bool isBatch)
        {
            throw new NotImplementedException();
        }

        public IModel GetModel(string connectionName)
        {
            throw new NotImplementedException();
        }

        public void Shutdown(TimeSpan timeout)
        {
            throw new NotImplementedException();
        }
    }
}
