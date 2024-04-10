// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal class ZipContainer : Container
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
                Resources.OpeningContainer,
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
                Resources.SavingContainer,
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