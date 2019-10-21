
using Microservices.CohortExtractor.Audit;
using Microservices.Common.Messages.Extraction;
using System.Collections.Generic;

namespace Microservices.CohortExtractor.Execution.RequestFulfillers
{
    public interface IExtractionRequestFulfiller
    {
        /// <summary>
        /// When implemented in a derived class will connect to data sources and return all dicom files on disk which
        /// correspond to the identifiers in the <paramref name="message"/>.
        /// </summary>
        /// <param name="message">The request you want answered (contains the list of UIDs to extract)</param>
        /// <param name="auditor">The class we should inform of progress</param>
        /// <returns></returns>
        IEnumerable<ExtractImageCollection> GetAllMatchingFiles(ExtractionRequestMessage message, IAuditExtractions auditor);
    }
}