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

        public TemporaryZipFile(string inputFileName, string filter, ILogger logger, bool filterHasPath = true)
        {
            this.inputFileName = inputFileName;
            this.logger = logger;
            dataDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dataDirectory);




            logger.LogInformation($"Extracting zip file {inputFileName}");
            ZipFile.ExtractToDirectory(inputFileName, dataDirectory);

            var filesInDir = Directory.EnumerateFiles(dataDirectory, "*.*", SearchOption.AllDirectories);
            FilesInDirectory = filesInDir.ToList();

            if (filterHasPath)
            {
                var filterSet = new HashSet<string>(filter.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => Path.Combine(dataDirectory, s)), StringComparer.OrdinalIgnoreCase);

                if (filterSet.Count > 0)
                    FilteredFilesInDirectory = FilesInDirectory.Intersect(filterSet, StringComparer.OrdinalIgnoreCase).ToList();

            }
            else
            {
                var filterSet = new HashSet<string>(filter.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
                if (filterSet.Count > 0)
                // match on file name only
                {
                    FilteredFilesInDirectory = FilesInDirectory.Where(f => filterSet.Contains(Path.GetFileName(f))).ToList();
                }
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
