using Microservices.CohortPackager.Execution.ExtractJobStorage;
using Microservices.CohortPackager.Execution.JobProcessing.Reporting;
using Moq;
using NUnit.Framework;
using Smi.Common.Tests;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;


namespace Microservices.CohortPackager.Tests.Execution.JobProcessing.Reporting
{
    [TestFixture]
    public class JobReporterBaseTest
    {
        #region Fixture Methods 

        [OneTimeSetUp]
        public void OneTimeSetUp() { }

        [OneTimeTearDown]
        public void OneTimeTearDown() { }

        #endregion

        #region Test Methods

        [SetUp]
        public void SetUp() { }

        [TearDown]
        public void TearDown() { }

        #endregion

        #region Tests

        private class TestJobReporter : JobReporterBase
        {
            public string Report { get; set; } = "";

            public bool Disposed { get; set; }

            private string _currentReportName;
            private bool _isCombinedReport;

            public TestJobReporter(IExtractJobStore jobStore, ReportFormat reportFormat) : base(jobStore, reportFormat) { }

            protected override Stream GetStreamForSummary(ExtractJobInfo jobInfo)
            {
                _currentReportName = "summary";
                _isCombinedReport = ShouldWriteCombinedReport(jobInfo);
                return new MemoryStream();
            }

            protected override Stream GetStreamForPixelDataSummary(ExtractJobInfo jobInfo)
            {
                _currentReportName = "pixel summary";
                return new MemoryStream();
            }

            protected override Stream GetStreamForPixelDataFull(ExtractJobInfo jobInfo)
            {
                _currentReportName = "pixel full";
                return new MemoryStream();
            }

            protected override Stream GetStreamForPixelDataWordLengthFrequencies(ExtractJobInfo jobInfo)
            {
                _currentReportName = "pixel word length frequencies";
                return new MemoryStream();
            }

            protected override Stream GetStreamForTagDataSummary(ExtractJobInfo jobInfo)
            {
                _currentReportName = "tag summary";
                return new MemoryStream();
            }

            protected override Stream GetStreamForTagDataFull(ExtractJobInfo jobInfo)
            {
                _currentReportName = "tag full";
                return new MemoryStream();
            }

            protected override void FinishReportPart(Stream stream)
            {
                stream.Position = 0;
                using var streamReader = new StreamReader(stream, leaveOpen: true);
                string header = _isCombinedReport ? "" : $"\n=== {_currentReportName} file ===\n";
                Report += header + streamReader.ReadToEnd();
            }

            protected override void ReleaseUnmanagedResources() => Disposed = true;
            public override void Dispose() => ReleaseUnmanagedResources();
        }

        [Test]
        public void Test_JobReporterBase_CreateReport_Empty()
        {
            Guid jobId = Guid.NewGuid();
            var provider = new TestDateTimeProvider();
            var testJobInfo = new CompletedExtractJobInfo(
                jobId,
                provider.UtcNow(),
                provider.UtcNow() + TimeSpan.FromHours(1),
                "1234",
                "extractions/test",
                "keyTag",
                123,
                "ZZ",
                isIdentifiableExtraction: false,
                isNoFilterExtraction: false
            );

            var mockJobStore = new Mock<IExtractJobStore>(MockBehavior.Strict);
            mockJobStore.Setup(x => x.GetCompletedJobInfo(It.IsAny<Guid>())).Returns(testJobInfo);
            mockJobStore.Setup(x => x.GetCompletedJobRejections(It.IsAny<Guid>())).Returns(new List<ExtractionIdentifierRejectionInfo>());
            mockJobStore.Setup(x => x.GetCompletedJobAnonymisationFailures(It.IsAny<Guid>())).Returns(new List<FileAnonFailureInfo>());
            mockJobStore.Setup(x => x.GetCompletedJobVerificationFailures(It.IsAny<Guid>())).Returns(new List<FileVerificationFailureInfo>());

            TestJobReporter reporter;
            using (reporter = new TestJobReporter(mockJobStore.Object, ReportFormat.Combined))
            {
                reporter.CreateReport(Guid.Empty);
            }

            string expected = $@"
# SMI extraction validation report for 1234/test

Job info:
-   Job submitted at:              {provider.UtcNow().ToString("s", CultureInfo.InvariantCulture)}
-   Job completed at:              {(provider.UtcNow() + TimeSpan.FromHours(1)).ToString("s", CultureInfo.InvariantCulture)}
-   Job extraction id:             {jobId}
-   Extraction tag:                keyTag
-   Extraction modality:           ZZ
-   Requested identifier count:    123
-   Identifiable extraction:       No
-   Filtered extraction:           Yes

Report contents:

-   Verification failures
    -   Summary
    -   Full Details
-   Blocked files
-   Anonymisation failures

## Verification failures

### Summary


### Full details


## Blocked files


## Anonymisation failures


--- end of report ---
";
            TestHelpers.AreEqualIgnoringLineEndings(expected, reporter.Report);
            Assert.True(reporter.Disposed);
        }

        [Test]
        public void Test_JobReporterBase_CreateReport_WithBasicData()
        {
            Guid jobId = Guid.NewGuid();
            var provider = new TestDateTimeProvider();
            var testJobInfo = new CompletedExtractJobInfo(
                jobId,
                provider.UtcNow(),
                provider.UtcNow() + TimeSpan.FromHours(1),
                "1234",
                "extractions/test",
                "keyTag",
                123,
                "ZZ",
                isIdentifiableExtraction: false,
                isNoFilterExtraction: false
            );

            var rejections = new List<ExtractionIdentifierRejectionInfo>
            {
                new ExtractionIdentifierRejectionInfo(
                    keyValue: "1.2.3.4",
                    new Dictionary<string, int>
                    {
                        {"image is in the deny list for extraction", 123},
                        {"foo bar", 456},
                    }),
            };

            var anonFailures = new List<FileAnonFailureInfo>
            {
                new FileAnonFailureInfo(expectedAnonFile: "foo1.dcm", reason: "image was corrupt"),
            };

            const string report = @"
[
    {
        'Parts': [],
        'Resource': '/foo1.dcm',
        'ResourcePrimaryKey': '1.2.3.4',
        'ProblemField': 'ScanOptions',
        'ProblemValue': 'FOO'
    }
]";

            var verificationFailures = new List<FileVerificationFailureInfo>
            {
                new FileVerificationFailureInfo(anonFilePath: "foo1.dcm", report),
            };

            var mockJobStore = new Mock<IExtractJobStore>(MockBehavior.Strict);
            mockJobStore.Setup(x => x.GetCompletedJobInfo(It.IsAny<Guid>())).Returns(testJobInfo);
            mockJobStore.Setup(x => x.GetCompletedJobRejections(It.IsAny<Guid>())).Returns(rejections);
            mockJobStore.Setup(x => x.GetCompletedJobAnonymisationFailures(It.IsAny<Guid>())).Returns(anonFailures);
            mockJobStore.Setup(x => x.GetCompletedJobVerificationFailures(It.IsAny<Guid>())).Returns(verificationFailures);

            TestJobReporter reporter;
            using (reporter = new TestJobReporter(mockJobStore.Object, ReportFormat.Combined))
            {
                reporter.CreateReport(Guid.Empty);
            }

            string expected = $@"
# SMI extraction validation report for 1234/test

Job info:
-   Job submitted at:              {provider.UtcNow().ToString("s", CultureInfo.InvariantCulture)}
-   Job completed at:              {(provider.UtcNow() + TimeSpan.FromHours(1)).ToString("s", CultureInfo.InvariantCulture)}
-   Job extraction id:             {jobId}
-   Extraction tag:                keyTag
-   Extraction modality:           ZZ
-   Requested identifier count:    123
-   Identifiable extraction:       No
-   Filtered extraction:           Yes

Report contents:

-   Verification failures
    -   Summary
    -   Full Details
-   Blocked files
-   Anonymisation failures

## Verification failures

### Summary

-   Tag: ScanOptions (1 total occurrence(s))
    -   Value: 'FOO' (1 occurrence(s))


### Full details

-   Tag: ScanOptions (1 total occurrence(s))
    -   Value: 'FOO' (1 occurrence(s))
        -   foo1.dcm


## Blocked files

-   ID: 1.2.3.4
    -   456x 'foo bar'
    -   123x 'image is in the deny list for extraction'

## Anonymisation failures

-   file 'foo1.dcm': 'image was corrupt'

--- end of report ---
";

            TestHelpers.AreEqualIgnoringLineEndings(expected, reporter.Report);
            Assert.True(reporter.Disposed);
        }

        [Test]
        public void Test_JobReporterBase_WriteJobVerificationFailures_JsonException()
        {
            Guid jobId = Guid.NewGuid();
            var provider = new TestDateTimeProvider();
            var testJobInfo = new CompletedExtractJobInfo(
                jobId,
                provider.UtcNow(),
                provider.UtcNow() + TimeSpan.FromHours(1),
                "1234",
                "test/dir",
                "keyTag",
                123,
                "ZZ",
                isIdentifiableExtraction: false,
                isNoFilterExtraction: false
            );

            var verificationFailures = new List<FileVerificationFailureInfo>
            {
                new FileVerificationFailureInfo(anonFilePath: "foo1.dcm", failureData: "totally not a report"),
            };

            var mockJobStore = new Mock<IExtractJobStore>(MockBehavior.Strict);
            mockJobStore.Setup(x => x.GetCompletedJobInfo(It.IsAny<Guid>())).Returns(testJobInfo);
            mockJobStore.Setup(x => x.GetCompletedJobRejections(It.IsAny<Guid>())).Returns(new List<ExtractionIdentifierRejectionInfo>());
            mockJobStore.Setup(x => x.GetCompletedJobAnonymisationFailures(It.IsAny<Guid>())).Returns(new List<FileAnonFailureInfo>());
            mockJobStore.Setup(x => x.GetCompletedJobVerificationFailures(It.IsAny<Guid>())).Returns(verificationFailures);

            TestJobReporter reporter;
            using (reporter = new TestJobReporter(mockJobStore.Object, ReportFormat.Combined))
            {
                Assert.Throws<ApplicationException>(() => reporter.CreateReport(Guid.Empty), "aa");
            }

            Assert.True(reporter.Disposed);
        }

        [Test]
        public void Test_JobReporterBase_CreateReport_AggregateData()
        {
            Guid jobId = Guid.NewGuid();
            var provider = new TestDateTimeProvider();
            var testJobInfo = new CompletedExtractJobInfo(
                jobId,
                provider.UtcNow(),
                provider.UtcNow() + TimeSpan.FromHours(1),
                "1234",
                "extractions/test",
                "keyTag",
                123,
                "ZZ",
                isIdentifiableExtraction: false,
                isNoFilterExtraction: false
            );

            var verificationFailures = new List<FileVerificationFailureInfo>
            {
                new FileVerificationFailureInfo(anonFilePath: "ccc/ddd/foo1.dcm", failureData: @"
                    [
                        {
                             'Parts': [],
                            'Resource': 'unused',
                            'ResourcePrimaryKey': 'unused',
                            'ProblemField': 'SomeOtherTag',
                            'ProblemValue': 'BAZ'
                        }
                    ]"
                ),
                new FileVerificationFailureInfo(anonFilePath:"ccc/ddd/foo2.dcm",failureData: @"
                    [
                        {
                             'Parts': [],
                            'Resource': 'unused',
                            'ResourcePrimaryKey': 'unused',
                            'ProblemField': 'SomeOtherTag',
                            'ProblemValue': 'BAZ'
                        }
                    ]"
                ),
                new FileVerificationFailureInfo(anonFilePath:"aaa/bbb/foo1.dcm", failureData:@"
                    [
                        {
                            'Parts': [],
                            'Resource': 'unused',
                            'ResourcePrimaryKey': 'unused',
                            'ProblemField': 'ScanOptions',
                            'ProblemValue': 'FOO'
                        }
                    ]"
                ),
                new FileVerificationFailureInfo(anonFilePath:"aaa/bbb/foo2.dcm",failureData: @"
                    [
                        {
                            'Parts': [],
                            'Resource': 'unused',
                            'ResourcePrimaryKey': 'unused',
                            'ProblemField': 'ScanOptions',
                            'ProblemValue': 'FOO'
                        }
                    ]"
                ),
                new FileVerificationFailureInfo(anonFilePath:"aaa/bbb/foo2.dcm", failureData: @"
                    [
                         {
                            'Parts': [],
                            'Resource': 'unused',
                            'ResourcePrimaryKey': 'unused',
                            'ProblemField': 'ScanOptions',
                            'ProblemValue': 'BAR'
                        }
                    ]"
                ),
            };

            var mockJobStore = new Mock<IExtractJobStore>(MockBehavior.Strict);
            mockJobStore.Setup(x => x.GetCompletedJobInfo(It.IsAny<Guid>())).Returns(testJobInfo);
            mockJobStore.Setup(x => x.GetCompletedJobRejections(It.IsAny<Guid>())).Returns(new List<ExtractionIdentifierRejectionInfo>());
            mockJobStore.Setup(x => x.GetCompletedJobAnonymisationFailures(It.IsAny<Guid>())).Returns(new List<FileAnonFailureInfo>());
            mockJobStore.Setup(x => x.GetCompletedJobVerificationFailures(It.IsAny<Guid>()))
                .Returns(verificationFailures);

            TestJobReporter reporter;
            using (reporter = new TestJobReporter(mockJobStore.Object, ReportFormat.Combined))
            {
                reporter.CreateReport(Guid.Empty);
            }

            string expected = $@"
# SMI extraction validation report for 1234/test

Job info:
-   Job submitted at:              {provider.UtcNow().ToString("s", CultureInfo.InvariantCulture)}
-   Job completed at:              {(provider.UtcNow() + TimeSpan.FromHours(1)).ToString("s", CultureInfo.InvariantCulture)}
-   Job extraction id:             {jobId}
-   Extraction tag:                keyTag
-   Extraction modality:           ZZ
-   Requested identifier count:    123
-   Identifiable extraction:       No
-   Filtered extraction:           Yes

Report contents:

-   Verification failures
    -   Summary
    -   Full Details
-   Blocked files
-   Anonymisation failures

## Verification failures

### Summary

-   Tag: ScanOptions (3 total occurrence(s))
    -   Value: 'FOO' (2 occurrence(s))
    -   Value: 'BAR' (1 occurrence(s))

-   Tag: SomeOtherTag (2 total occurrence(s))
    -   Value: 'BAZ' (2 occurrence(s))


### Full details

-   Tag: ScanOptions (3 total occurrence(s))
    -   Value: 'FOO' (2 occurrence(s))
        -   aaa/bbb/foo1.dcm
        -   aaa/bbb/foo2.dcm
    -   Value: 'BAR' (1 occurrence(s))
        -   aaa/bbb/foo2.dcm

-   Tag: SomeOtherTag (2 total occurrence(s))
    -   Value: 'BAZ' (2 occurrence(s))
        -   ccc/ddd/foo1.dcm
        -   ccc/ddd/foo2.dcm


## Blocked files


## Anonymisation failures


--- end of report ---
";
            TestHelpers.AreEqualIgnoringLineEndings(expected, reporter.Report);
            Assert.True(reporter.Disposed);
        }

        [Test]
        public void Test_JobReporterBase_CreateReport_WithPixelData()
        {
            // NOTE(rkm 2020-08-25) Tests that the "Z" tag is ordered before PixelData, and that PixelData items are ordered by decreasing length not by occurrence

            Guid jobId = Guid.NewGuid();
            var provider = new TestDateTimeProvider();
            var testJobInfo = new CompletedExtractJobInfo(
                jobId,
                provider.UtcNow(),
                provider.UtcNow() + TimeSpan.FromHours(1),
                "1234",
                "extractions/test",
                "keyTag",
                123,
                "ZZ",
                isIdentifiableExtraction: false,
                isNoFilterExtraction: false
            );

            const string report = @"
[
     {
        'Parts': [],
        'Resource': 'unused',
        'ResourcePrimaryKey': 'unused',
        'ProblemField': 'PixelData',
        'ProblemValue': 'aaaaaaaaaaa'
    },
    {
        'Parts': [],
        'Resource': 'unused',
        'ResourcePrimaryKey': 'unused',
        'ProblemField': 'PixelData',
        'ProblemValue': 'a'
    },
    {
        'Parts': [],
        'Resource': 'unused',
        'ResourcePrimaryKey': 'unused',
        'ProblemField': 'PixelData',
        'ProblemValue': 'a'
    },
    {
        'Parts': [],
        'Resource': 'unused',
        'ResourcePrimaryKey': 'unused',
        'ProblemField': 'Z',
        'ProblemValue': 'bar'
    },
]";

            var verificationFailures = new List<FileVerificationFailureInfo>
            {
                new FileVerificationFailureInfo(anonFilePath: "foo1.dcm", report),
            };

            var mockJobStore = new Mock<IExtractJobStore>(MockBehavior.Strict);
            mockJobStore.Setup(x => x.GetCompletedJobInfo(It.IsAny<Guid>())).Returns(testJobInfo);
            mockJobStore.Setup(x => x.GetCompletedJobRejections(It.IsAny<Guid>())).Returns(new List<ExtractionIdentifierRejectionInfo>());
            mockJobStore.Setup(x => x.GetCompletedJobAnonymisationFailures(It.IsAny<Guid>())).Returns(new List<FileAnonFailureInfo>());
            mockJobStore.Setup(x => x.GetCompletedJobVerificationFailures(It.IsAny<Guid>())).Returns(verificationFailures);

            TestJobReporter reporter;
            using (reporter = new TestJobReporter(mockJobStore.Object, ReportFormat.Combined))
            {
                reporter.CreateReport(Guid.Empty);
            }

            string expected = $@"
# SMI extraction validation report for 1234/test

Job info:
-   Job submitted at:              {provider.UtcNow().ToString("s", CultureInfo.InvariantCulture)}
-   Job completed at:              {(provider.UtcNow() + TimeSpan.FromHours(1)).ToString("s", CultureInfo.InvariantCulture)}
-   Job extraction id:             {jobId}
-   Extraction tag:                keyTag
-   Extraction modality:           ZZ
-   Requested identifier count:    123
-   Identifiable extraction:       No
-   Filtered extraction:           Yes

Report contents:

-   Verification failures
    -   Summary
    -   Full Details
-   Blocked files
-   Anonymisation failures

## Verification failures

### Summary

-   Tag: Z (1 total occurrence(s))
    -   Value: 'bar' (1 occurrence(s))

-   Tag: PixelData (3 total occurrence(s))
    -   Value: 'aaaaaaaaaaa' (1 occurrence(s))
    -   Value: 'a' (2 occurrence(s))


### Full details

-   Tag: Z (1 total occurrence(s))
    -   Value: 'bar' (1 occurrence(s))
        -   foo1.dcm

-   Tag: PixelData (3 total occurrence(s))
    -   Value: 'aaaaaaaaaaa' (1 occurrence(s))
        -   foo1.dcm
    -   Value: 'a' (2 occurrence(s))
        -   foo1.dcm
        -   foo1.dcm


## Blocked files


## Anonymisation failures


--- end of report ---
";
            TestHelpers.AreEqualIgnoringLineEndings(expected, reporter.Report);
            Assert.True(reporter.Disposed);
        }

        [Test]
        public void Test_JobReporterBase_CreateReport_IdentifiableExtraction()
        {
            Guid jobId = Guid.NewGuid();
            var provider = new TestDateTimeProvider();
            var testJobInfo = new CompletedExtractJobInfo(
                jobId,
                provider.UtcNow(),
                provider.UtcNow() + TimeSpan.FromHours(1),
                "1234",
                "extractions/test",
                "keyTag",
                123,
                "ZZ",
                isIdentifiableExtraction: true,
                isNoFilterExtraction: false
            );

            var missingFiles = new List<string>
            {
               "missing.dcm",
            };

            var mockJobStore = new Mock<IExtractJobStore>(MockBehavior.Strict);
            mockJobStore.Setup(x => x.GetCompletedJobInfo(It.IsAny<Guid>())).Returns(testJobInfo);
            mockJobStore.Setup(x => x.GetCompletedJobMissingFileList(It.IsAny<Guid>())).Returns(missingFiles);

            TestJobReporter reporter;
            using (reporter = new TestJobReporter(mockJobStore.Object, ReportFormat.Combined))
            {
                reporter.CreateReport(Guid.Empty);
            }

            string expected = $@"
# SMI extraction validation report for 1234/test

Job info:
-   Job submitted at:              {provider.UtcNow().ToString("s", CultureInfo.InvariantCulture)}
-   Job completed at:              {(provider.UtcNow() + TimeSpan.FromHours(1)).ToString("s", CultureInfo.InvariantCulture)}
-   Job extraction id:             {jobId}
-   Extraction tag:                keyTag
-   Extraction modality:           ZZ
-   Requested identifier count:    123
-   Identifiable extraction:       Yes
-   Filtered extraction:           Yes

Report contents:
-   Missing file list (files which were selected from an input ID but could not be found)

## Missing file list

-   missing.dcm

--- end of report ---
";
            TestHelpers.AreEqualIgnoringLineEndings(expected, reporter.Report);
            Assert.True(reporter.Disposed);
        }


        [Test]
        public void Test_JobReporterBase_CreateReport_FilteredExtraction()
        {
            Guid jobId = Guid.NewGuid();
            var provider = new TestDateTimeProvider();
            var testJobInfo = new CompletedExtractJobInfo(
                jobId,
                provider.UtcNow(),
                provider.UtcNow() + TimeSpan.FromHours(1),
                "1234",
                "extractions/test",
                "keyTag",
                123,
                "ZZ",
                isIdentifiableExtraction: false,
                isNoFilterExtraction: true
                );

            var mockJobStore = new Mock<IExtractJobStore>(MockBehavior.Strict);
            mockJobStore.Setup(x => x.GetCompletedJobInfo(It.IsAny<Guid>())).Returns(testJobInfo);
            mockJobStore.Setup(x => x.GetCompletedJobRejections(It.IsAny<Guid>())).Returns(new List<ExtractionIdentifierRejectionInfo>());
            mockJobStore.Setup(x => x.GetCompletedJobAnonymisationFailures(It.IsAny<Guid>())).Returns(new List<FileAnonFailureInfo>());
            mockJobStore.Setup(x => x.GetCompletedJobVerificationFailures(It.IsAny<Guid>())).Returns(new List<FileVerificationFailureInfo>());

            TestJobReporter reporter;
            using (reporter = new TestJobReporter(mockJobStore.Object, ReportFormat.Combined))
            {
                reporter.CreateReport(Guid.Empty);
            }

            string expected = $@"
# SMI extraction validation report for 1234/test

Job info:
-   Job submitted at:              {provider.UtcNow().ToString("s", CultureInfo.InvariantCulture)}
-   Job completed at:              {(provider.UtcNow() + TimeSpan.FromHours(1)).ToString("s", CultureInfo.InvariantCulture)}
-   Job extraction id:             {jobId}
-   Extraction tag:                keyTag
-   Extraction modality:           ZZ
-   Requested identifier count:    123
-   Identifiable extraction:       No
-   Filtered extraction:           No

Report contents:

-   Verification failures
    -   Summary
    -   Full Details
-   Blocked files
-   Anonymisation failures

## Verification failures

### Summary


### Full details


## Blocked files


## Anonymisation failures


--- end of report ---
";
            TestHelpers.AreEqualIgnoringLineEndings(expected, reporter.Report);
            Assert.True(reporter.Disposed);
        }

        [Test]
        public void CreateReport_SplitReport()
        {
            Guid jobId = Guid.NewGuid();
            var provider = new TestDateTimeProvider();
            var testJobInfo = new CompletedExtractJobInfo(
                jobId,
                provider.UtcNow(),
                provider.UtcNow() + TimeSpan.FromHours(1),
                "1234",
                "extractions/test",
                "keyTag",
                123,
                "ZZ",
                isIdentifiableExtraction: false,
                isNoFilterExtraction: false
            );

            const string report = @"
[
     {
        'Parts': [],
        'Resource': 'unused',
        'ResourcePrimaryKey': 'unused',
        'ProblemField': 'PixelData',
        'ProblemValue': 'aaaaaaaaaaa'
    },
    {
        'Parts': [],
        'Resource': 'unused',
        'ResourcePrimaryKey': 'unused',
        'ProblemField': 'PixelData',
        'ProblemValue': 'a'
    },
    {
        'Parts': [],
        'Resource': 'unused',
        'ResourcePrimaryKey': 'unused',
        'ProblemField': 'PixelData',
        'ProblemValue': 'a'
    },
    {
        'Parts': [],
        'Resource': 'unused',
        'ResourcePrimaryKey': 'unused',
        'ProblemField': 'PixelData',
        'ProblemValue': 'another'
    },
    {
        'Parts': [],
        'Resource': 'unused',
        'ResourcePrimaryKey': 'unused',
        'ProblemField': 'X',
        'ProblemValue': 'foo'
    },
    {
        'Parts': [],
        'Resource': 'unused',
        'ResourcePrimaryKey': 'unused',
        'ProblemField': 'X',
        'ProblemValue': 'foo'
    },
    {
        'Parts': [],
        'Resource': 'unused',
        'ResourcePrimaryKey': 'unused',
        'ProblemField': 'X',
        'ProblemValue': 'bar'
    },
    {
        'Parts': [],
        'Resource': 'unused',
        'ResourcePrimaryKey': 'unused',
        'ProblemField': 'Z',
        'ProblemValue': 'bar'
    },
]";

            var verificationFailures = new List<FileVerificationFailureInfo>
            {
                new FileVerificationFailureInfo(anonFilePath: "foo1.dcm", report),
                new FileVerificationFailureInfo(anonFilePath: "foo2.dcm", report),
            };

            var mockJobStore = new Mock<IExtractJobStore>(MockBehavior.Strict);
            mockJobStore.Setup(x => x.GetCompletedJobInfo(It.IsAny<Guid>())).Returns(testJobInfo);
            mockJobStore.Setup(x => x.GetCompletedJobRejections(It.IsAny<Guid>())).Returns(new List<ExtractionIdentifierRejectionInfo>());
            mockJobStore.Setup(x => x.GetCompletedJobAnonymisationFailures(It.IsAny<Guid>())).Returns(new List<FileAnonFailureInfo>());
            mockJobStore.Setup(x => x.GetCompletedJobVerificationFailures(It.IsAny<Guid>())).Returns(verificationFailures);

            TestJobReporter reporter;
            using (reporter = new TestJobReporter(mockJobStore.Object, ReportFormat.Split))
            {
                reporter.CreateReport(Guid.Empty);
            }

            // Test ordering of multiple tags / multiple occurrences in each tag
            var expected = $@"
=== summary file ===
# SMI extraction validation report for 1234/test

Job info:
-   Job submitted at:              {provider.UtcNow().ToString("s", CultureInfo.InvariantCulture)}
-   Job completed at:              {(provider.UtcNow() + TimeSpan.FromHours(1)).ToString("s", CultureInfo.InvariantCulture)}
-   Job extraction id:             {jobId}
-   Extraction tag:                keyTag
-   Extraction modality:           ZZ
-   Requested identifier count:    123
-   Identifiable extraction:       No
-   Filtered extraction:           Yes

Files included:
-   README.md (this file)
-   pixel_data_summary.csv
-   pixel_data_full.csv
-   pixel_data_word_length_frequencies.csv
-   tag_data_summary.csv
-   tag_data_full.csv

This file contents:
-   Blocked files
-   Anonymisation failures

## Blocked files


## Anonymisation failures


--- end of report ---

=== pixel summary file ===
TagName,FailureValue,Occurrences,RelativeFrequencyInTag,RelativeFrequencyInReport
PixelData,aaaaaaaaaaa,2,0.25,0.25
PixelData,another,2,0.25,0.25
PixelData,a,4,0.5,0.5

=== pixel full file ===
TagName,FailureValue,FilePath
PixelData,aaaaaaaaaaa,foo1.dcm
PixelData,aaaaaaaaaaa,foo2.dcm
PixelData,another,foo1.dcm
PixelData,another,foo2.dcm
PixelData,a,foo1.dcm
PixelData,a,foo1.dcm
PixelData,a,foo2.dcm
PixelData,a,foo2.dcm

=== pixel word length frequencies file ===
WordLength,Count,RelativeFrequencyInReport
1,4,0.5
2,0,0
3,0,0
4,0,0
5,0,0
6,0,0
7,2,0.25
8,0,0
9,0,0
10,0,0
11,2,0.25

=== tag summary file ===
TagName,FailureValue,Occurrences,RelativeFrequencyInTag,RelativeFrequencyInReport
X,foo,4,0.6666666666666666,0.5
X,bar,2,0.3333333333333333,0.5
Z,bar,2,1,0.5

=== tag full file ===
TagName,FailureValue,FilePath
X,foo,foo1.dcm
X,foo,foo1.dcm
X,foo,foo2.dcm
X,foo,foo2.dcm
X,bar,foo1.dcm
X,bar,foo2.dcm
Z,bar,foo1.dcm
Z,bar,foo2.dcm
";
            Console.WriteLine(reporter.Report);
            TestHelpers.AreEqualIgnoringLineEndings(expected, reporter.Report);
            Assert.True(reporter.Disposed);
        }
    }

    #endregion
}
