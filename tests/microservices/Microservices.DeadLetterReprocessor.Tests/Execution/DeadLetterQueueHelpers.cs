﻿
using NUnit.Framework;
using Smi.Common.Messages;
using Smi.Common.Tests;
using Smi.Common.Tests.DeadLetterMessagingTests;
using System.Threading;


namespace Microservices.Tests.DeadLetterReprocessorTests.Execution
{
    [TestFixture, RequiresRabbit]
    public class DeadLetterQueueHelpers
    {
        private readonly DeadLetterTestHelper _testHelper = new DeadLetterTestHelper();

        #region Fixture Methods 

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _testHelper.SetUpSuite();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _testHelper.DeleteRabbitBitsOnDispose = false;
            _testHelper.Dispose();
        }

        #endregion

        #region Test Methods

        [SetUp]
        public void SetUp()
        {
            _testHelper.ResetSuite();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            _testHelper.Dispose();
        }

        #endregion

        #region Tests

        /// <summary>
        /// Helper test to populate the dead letter queue with some messages
        /// </summary>
        /// <param name="nMessages"></param>
        [Test]
        [TestCase(10)]
        public void PopulateDeadLetterQueue(int nMessages)
        {
            var testMessage = new AccessionDirectoryMessage
            {
                NationalPACSAccessionNumber = "1234",
                DirectoryPath = @"C:\temp"
            };

            for (var i = 0; i < nMessages; ++i)
                _testHelper.TestProducer.SendMessage(testMessage, null, DeadLetterTestHelper.TestRoutingKey);

            new TestTimelineAwaiter().Await(() => _testHelper.TestModel.MessageCount(DeadLetterTestHelper.TestDlQueueName) == nMessages);
        }

        /// <summary>
        /// Helper test to run the MessageRejector consumer for some time
        /// </summary>
        [Test]
        [TestCase(0)]
        public void RunMessageRejector(int nSeconds)
        {
            Thread.Sleep(nSeconds * 1000);
        }
        #endregion
    }
}
