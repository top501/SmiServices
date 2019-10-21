﻿
using Microservices.CohortPackager.Execution.ExtractJobStorage;
using Microservices.Common.Messages;
using Microservices.Common.Messages.Extraction;
using Microservices.Common.Messaging;
using RabbitMQ.Client.Events;
using System;

namespace Microservices.CohortPackager.Messaging
{
    /// <summary>
    /// Consumer for <see cref="ExtractFileStatusMessage"/>(s)
    /// </summary>
    public class ExtractFileStatusMessageConsumer : Consumer
    {
        private readonly IExtractJobStore _store;


        public ExtractFileStatusMessageConsumer(IExtractJobStore store)
        {
            _store = store;
        }


        protected override void ProcessMessageImpl(IMessageHeader header, BasicDeliverEventArgs ea)
        {
            ExtractFileStatusMessage message;
            if (!SafeDeserializeToMessage(header, ea, out message))
                return;

            try
            {
                _store.PersistMessageToStore(message, header);
            }
            catch (ApplicationException e)
            {
                // Catch specific exceptions we are aware of, any uncaught will bubble up to the wrapper in ProcessMessage

                ErrorAndNack(header, ea, "Error while processing ExtractFileStatusMessage", e);
                return;
            }

            Ack(header, ea);
        }
    }
}