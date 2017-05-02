using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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

            var filterSet = new HashSet<string>(filter.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => Path.Combine(dataDirectory, s)), StringComparer.OrdinalIgnoreCase);

            logger.LogInformation($"Extracting zip file {inputFileName}");
            ZipFile.ExtractToDirectory(inputFileName, dataDirectory);

            var filesInDir = Directory.EnumerateFiles(dataDirectory, "*.*", SearchOption.AllDirectories);
            if (filterSet.Count > 0)
                filesInDir = filesInDir.Intersect(filterSet, StringComparer.OrdinalIgnoreCase);

            FilteredFilesInDirectory = filesInDir.ToList();
        }

        public void Save()
        {
            logger.LogInformation($"Building signed {inputFileName}");
            File.Delete(inputFileName);
            ZipFile.CreateFromDirectory(dataDirectory, inputFileName, CompressionLevel.Optimal, false);
        }

        public IList<string> FilteredFilesInDirectory { get; }

        public void Dispose()
        {
            Directory.Delete(dataDirectory, true);
        }

    }
}
