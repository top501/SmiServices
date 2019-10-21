﻿using System.Linq;
using Dicom;
using Microservices.DicomRelationalMapper.Messaging;
using System.Collections.Generic;
using Rdmp.Dicom.PipelineComponents.DicomSources.Worklists;

namespace Microservices.DicomRelationalMapper.Execution
{
    public class DicomFileMessageToDatasetListWorklist : IDicomDatasetWorklist
    {
        private readonly List<QueuedImage> _messages;
        private int _progress;

        public HashSet<QueuedImage> CorruptMessages = new HashSet<QueuedImage>();

        public DicomFileMessageToDatasetListWorklist(List<QueuedImage> messages)
        {
            _messages = messages;
        }

        public DicomDataset GetNextDatasetToProcess(out string filename, out Dictionary<string, string> otherValuesToStoreInRow)
        {
            otherValuesToStoreInRow = new Dictionary<string, string>();

            if (_progress >= _messages.Count)
            {
                filename = null;
                return null;
            }

            QueuedImage toReturn = _messages[_progress];
            filename = toReturn.DicomFileMessage.DicomFilePath;

            otherValuesToStoreInRow.Add("MessageGuid", _messages[_progress].Header.MessageGuid.ToString());

            _progress++;

            return toReturn.DicomDataset;
        }

        public void MarkCorrupt(DicomDataset ds)
        {
            CorruptMessages.Add(_messages.Single(m=>m.DicomDataset == ds));
        }
    }
}