using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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

            var globs = filter.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            if (globs.Count > 0)
            {
                var files = Globber.GetFiles(new DirectoryInfo(dataDirectory), globs);
                FilteredFilesInDirectory = files.Select(f => f.FullName).ToList();
            }

            // If no filtered, default to all
            if (FilteredFilesInDirectory == null)
                FilteredFilesInDirectory = FilesInDirectory.ToList();

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
            Directory.Delete(dataDirectory, true);
        }

    }
}
