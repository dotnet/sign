using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Extensions.Logging;
using SignService.Utils;
using Wyam.Core.IO.Globbing;

namespace SignService
{
    public class TemporaryZipFile : IDisposable
    {
        readonly string inputFileName;
        readonly ILogger logger;
        readonly string dataDirectory;

        public TemporaryZipFile(string inputFileName, string filter, ILogger logger)
        {
            this.inputFileName = inputFileName;
            this.logger = logger;
            dataDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dataDirectory);




            logger.LogInformation($"Extracting zip file {inputFileName}");
            ZipFile.ExtractToDirectory(inputFileName, dataDirectory);

            var filesInDir = Directory.EnumerateFiles(dataDirectory, "*.*", SearchOption.AllDirectories);
            FilesInDirectory = filesInDir.ToList();

            // don't allow parent directory traversal
            filter = filter.Replace(@"..\", "").Replace("../", "");

            var globs = filter.Split('\n').Where(s => (!string.IsNullOrWhiteSpace(s)))
                                          .Where(s => (!s.StartsWith("!")))
                                          .ToList();

            var antiglobs = filter.Split('\n').Where(s => (!string.IsNullOrWhiteSpace(s)))
                                              .Where(s => (s.StartsWith("!")))
                                              .Select(s => s.Substring(1))
                                              .ToList();

            if (globs.Count > 0)
            {
                var files = Globber.GetFiles(new DirectoryInfo(dataDirectory), globs);
                FilteredFilesInDirectory = files.Select(f => f.FullName).ToList();
            }

            // If no filtered, default to all
            if (FilteredFilesInDirectory == null)
            {
                FilteredFilesInDirectory = FilesInDirectory.ToList();
            }

            if (antiglobs.Count > 0)
            {
                var antifiles = Globber.GetFiles(new DirectoryInfo(dataDirectory), antiglobs)
                                       .Select(f => f.FullName)
                                       .ToList();

                FilteredFilesInDirectory = FilteredFilesInDirectory.Except(antifiles).ToList();
            }

            FilesExceptFiltered = FilesInDirectory.Except(FilteredFilesInDirectory).ToList();
        }

        public void Save()
        {
            logger.LogInformation($"Building signed {inputFileName}");
            File.Delete(inputFileName);
            ZipFile.CreateFromDirectory(dataDirectory, inputFileName, CompressionLevel.Optimal, false);
        }

        public IList<string> FilteredFilesInDirectory { get; }
        public IList<string> FilesInDirectory { get; }
        public IList<string> FilesExceptFiltered { get; }

        public void Dispose()
        {
            DirectoryUtility.SafeDelete(dataDirectory);
        }

    }
}
