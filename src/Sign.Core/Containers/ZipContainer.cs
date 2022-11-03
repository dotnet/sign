using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class ZipContainer : Container
    {
        private readonly IDirectoryService _directoryService;
        private readonly ILogger _logger;
        private readonly FileInfo _zipFile;

        internal ZipContainer(
            FileInfo zipFile,
            IDirectoryService directoryService,
            IFileMatcher fileMatcher,
            ILogger logger)
            : base(fileMatcher)
        {
            ArgumentNullException.ThrowIfNull(zipFile, nameof(zipFile));
            ArgumentNullException.ThrowIfNull(directoryService, nameof(directoryService));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _directoryService = directoryService;
            _logger = logger;
            _zipFile = zipFile;
        }

        public override ValueTask OpenAsync()
        {
            if (TemporaryDirectory is not null)
            {
                throw new InvalidOperationException();
            }

            TemporaryDirectory = new TemporaryDirectory(_directoryService);

            _logger.LogInformation(
                "Extracting container file {ZipFilePath} to {DirectoryPath}.",
                _zipFile.FullName,
                TemporaryDirectory.Directory.FullName);

            ZipFile.ExtractToDirectory(_zipFile.FullName, TemporaryDirectory.Directory.FullName);

            return ValueTask.CompletedTask;
        }

        public override ValueTask SaveAsync()
        {
            if (TemporaryDirectory is null)
            {
                throw new InvalidOperationException();
            }

            _logger.LogInformation(
                "Rebuilding container {ZipFilePath} from {DirectoryPath}.",
                _zipFile.FullName,
                TemporaryDirectory.Directory.FullName);

            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                string destinationFilePath = Path.Combine(temporaryDirectory.Directory.FullName, _zipFile.Name);

                ZipFile.CreateFromDirectory(TemporaryDirectory.Directory.FullName, destinationFilePath, CompressionLevel.Optimal, false);

                _zipFile.Delete();

                File.Move(destinationFilePath, _zipFile.FullName, overwrite: true);

                _zipFile.Refresh();
            }

            return ValueTask.CompletedTask;
        }
    }
}