﻿
using Smi.Common;
using Smi.Common.Messaging;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;

namespace Applications.DicomDirectoryProcessor.Execution.DirectoryFinders
{
    /// <summary>
    /// Finds directories that contain zip files or dicom files.  Does not require files to be in a directory structure
    /// that contains AccessionNumber
    /// </summary>
    public class ZipDicomDirectoryFinder : BasicDicomDirectoryFinder
    {
        public ZipDicomDirectoryFinder(string fileSystemRoot, IFileSystem fileSystem, string dicomSearchPattern, IProducerModel directoriesProducerModel)
        : base(fileSystemRoot, fileSystem, dicomSearchPattern, directoriesProducerModel) 
            {
                AlwaysSearchSubdirectories = true;
            }

        public ZipDicomDirectoryFinder(string fileSystemRoot, string dicomSearchPattern, IProducerModel directoriesProducerModel)
            : this(fileSystemRoot, new FileSystem(), dicomSearchPattern, directoriesProducerModel) { }


        protected override IEnumerable<IFileInfo> GetEnumerator(IDirectoryInfo dirInfo)
        {
            return dirInfo.EnumerateFiles().Where(f=>f.Extension == ".dcm" || ZipHelper.IsZip(f));
        }

        /// <inheritdoc/>
        protected override string GetNationalPACSAccessionNumberFrom(string dirPath)
        {
            return null;
        }
    }
}
