﻿using System;
using Dicom;
using DicomTypeTranslation;
using Microservices.Common.Messages;
using Microservices.Common.Options;
using MongoDB.Driver;
using Moq;
using NLog;
using NUnit.Framework;
using Smi.MongoDB.Common;


namespace Microservices.Tests.MongoDBPopulatorTests
{
    public class MongoDbPopulatorTestHelper
    {
        public const string TestDbName = "nUnitTests";

        private MongoClient _mongoTestClient;

        public readonly ILogger MockLogger = Mock.Of<ILogger>();
        public IMongoDatabase TestDatabase;

        public GlobalOptions Globals;

        public DicomFileMessage TestImageMessage;
        public SeriesMessage TestSeriesMessage;

        public void SetupSuite()
        {
            Globals = GetNewMongoDbPopulatorOptions();

            _mongoTestClient = MongoClientHelpers.GetMongoClient(Globals.MongoDatabases.DicomStoreOptions, "MongoDbPopulatorTests");

            _mongoTestClient.DropDatabase(TestDbName);
            TestDatabase = _mongoTestClient.GetDatabase(TestDbName);

            Globals.MongoDbPopulatorOptions.SeriesQueueConsumerOptions = new ConsumerOptions()
            {
                QueueName = "TEST.SeriesQueue",
                QoSPrefetchCount = 5,
                AutoAck = false
            };

            Globals.MongoDbPopulatorOptions.ImageQueueConsumerOptions = new ConsumerOptions()
            {
                QueueName = "TEST.MongoImageQueue",
                QoSPrefetchCount = 50,
                AutoAck = false
            };

            var dataset = new DicomDataset
            {
                new DicomUniqueIdentifier(DicomTag.SOPInstanceUID, "1.2.3.4"),
                new DicomCodeString(DicomTag.Modality, "SR")
            };

            var serialized = DicomTypeTranslater.SerializeDatasetToJson(dataset);

            TestImageMessage = new DicomFileMessage
            {
                DicomFilePath = @"Path/To/File",
                NationalPACSAccessionNumber = "123",
                SeriesInstanceUID = "TestSeriesInstanceUID",
                StudyInstanceUID = "TestStudyInstanceUID",
                SOPInstanceUID = "TestSOPInstanceUID",
                DicomDataset = serialized
            };

            TestSeriesMessage = new SeriesMessage
            {
                DirectoryPath = @"Path/To/Series",
                ImagesInSeries = 123,
                NationalPACSAccessionNumber = "123",
                SeriesInstanceUID = "TestSeriesInstanceUID",
                StudyInstanceUID = "TestStudyInstanceUID",
                DicomDataset = serialized
            };

            new ProducerOptions()
            {
                ExchangeName = "TEST.FataLoggingExchange"
            };
        }

        public GlobalOptions GetNewMongoDbPopulatorOptions()
        {
            var options = GlobalOptions.Load("default.yaml", TestContext.CurrentContext.TestDirectory);

            options.MongoDatabases.DicomStoreOptions.DatabaseName = TestDbName;
            options.MongoDbPopulatorOptions.MongoDbFlushTime = 1; //1 second

            return options;
        }

        public void Dispose()
        {
            _mongoTestClient.DropDatabase(TestDbName);
        }

        public string GetCollectionNameForTest(string testName)
        {
            return testName + "-" + Guid.NewGuid();
        }
    }
}