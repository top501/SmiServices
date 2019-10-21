﻿
using System;
using System.Collections.Generic;
using System.Linq;
using Dicom;
using DicomTypeTranslation;
using Microservices.Common.Messages;
using Microservices.Common.Options;
using MongoDB.Bson;
using NLog;
using Smi.MongoDB.Common;


namespace Microservices.MongoDBPopulator.Execution.Processing
{
    /// <summary>
    /// Delegate class used to perform the actual processing of messages
    /// </summary>
    public class ImageMessageProcessor : MessageProcessor<DicomFileMessage>
    {
        private const string MongoLogMessage = "Added to write queue";

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        /// <param name="mongoDbAdapter"></param>
        /// <param name="maxQueueSize"></param>
        /// <param name="exceptionCallback"></param>
        public ImageMessageProcessor(MongoDbPopulatorOptions options, IMongoDbAdapter mongoDbAdapter, int maxQueueSize, Action<Exception> exceptionCallback)
            : base(options, mongoDbAdapter, maxQueueSize, exceptionCallback) { }


        public override void AddToWriteQueue(DicomFileMessage message, IMessageHeader header, ulong deliveryTag)
        {
            // Only time we are not processing is if we are shutting down anyway
            if (!IsProcessing)
                return;

            if (Model == null)
                throw new ApplicationException("Model needs to be set before messages can be processed");

            DicomDataset dataset;

            try
            {
                dataset = DicomTypeTranslater.DeserializeJsonToDataset(message.DicomDataset);
            }
            catch (Exception e)
            {
                throw new ApplicationException("Could not deserialize json to dataset", e);
            }

            BsonDocument datasetDoc;

            try
            {
                datasetDoc = DicomTypeTranslaterReader.BuildBsonDocument(dataset);
            }
            catch (Exception e)
            {
                throw new ApplicationException("Exception converting dataset to BsonDocument", e);
            }

            // Generate a new header to record the current service before storing in MongoDB
            var newHeader = new MessageHeader(header);
            newHeader.Log(Logger, LogLevel.Trace, MongoLogMessage);

            BsonDocument bsonHeader = MongoDocumentHeaders.ImageDocumentHeader(message, newHeader);

            BsonDocument document = new BsonDocument()
                                .Add("header", bsonHeader)
                                .AddRange(datasetDoc);

            int docByteLength = document.ToBson().Length;

            if (docByteLength > MaxDocumentSize)
                throw new ApplicationException("BsonDocument was larger than the max allowed size (have " + docByteLength + ", max is " + MaxDocumentSize + ")");

            var forceProcess = false;

            lock (LockObj)
            {
                ToProcess.Enqueue(new Tuple<BsonDocument, ulong>(document, deliveryTag));

                if (ToProcess.Count >= MaxQueueSize)
                    forceProcess = true;
            }

            if (!forceProcess)
                return;

            Logger.Debug("ImageMessageProcessor: Max queue size reached, calling ProcessQueue");
            ProcessQueue();
        }

        /// <summary>
        /// Writes all messages currently in the queue to MongoDb and acknowledges
        /// </summary>
        protected override void ProcessQueue()
        {
            // Will happen when ProcessQueue is called due to the timer, before we receive our first message
            if (Model == null)
                return;

            lock (LockObj)
            {
                if (ToProcess.Count == 0)
                    return;

                Logger.Info($"Queue contains {ToProcess.Count} message to write");

                foreach ((string modality, List<BsonDocument> modalityDocs) in
                    MongoModalityGroups.GetModalityChunks(ToProcess.Select(x => x.Item1).ToList()))
                {
                    Logger.Debug($"Attempting to write {modalityDocs.Count} documents of modality {modality}");

                    while (FailedWriteAttempts < FailedWriteLimit)
                    {
                        WriteResult imageWriteResult = MongoDbAdapter.WriteMany(modalityDocs, modality);

                        if (imageWriteResult == WriteResult.Success)
                        {
                            Logger.Debug($"Wrote {modalityDocs.Count} documents successfully, sending ACKs");

                            // Hopefully this uses ReferenceEquals, otherwise will be slow...
                            foreach (ulong deliveryTag in ToProcess
                                    .Where(x => modalityDocs.Contains(x.Item1))
                                    .Select(x => x.Item2))
                            {
                                Model.BasicAck(deliveryTag, false);
                            }

                            AckCount += modalityDocs.Count;
                            FailedWriteAttempts = 0;
                            break;
                        }

                        Logger.Warn($"Failed to write, {(FailedWriteAttempts + 1)} time(s) in a row");

                        if (++FailedWriteAttempts < FailedWriteLimit)
                            continue;

                        throw new ApplicationException("Failed write attempts exceeded");
                    }
                }

                Logger.Debug("Wrote and acknowledged all documents in queue. Clearing and continutig");
                ToProcess.Clear();
            }
        }

        public override void StopProcessing(string reason)
        {
            Logger.Debug("ImageMessageProcessor: Stopping (" + reason + ")");
            StopProcessing();
        }
    }
}
