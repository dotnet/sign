// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using WixToolset.Dtf.Compression;
using WixToolset.Dtf.Compression.Cab;

namespace Sign.Core
{
    internal class CabContainer : Container
    {
        private readonly IDirectoryService _directoryService;
        private readonly ILogger _logger;
        private readonly FileInfo _cabFile;

        internal CabContainer(
            FileInfo cabFile,
            IDirectoryService directoryService,
            IFileMatcher fileMatcher,
            ILogger logger)
            : base(fileMatcher)
        {
            ArgumentNullException.ThrowIfNull(cabFile, nameof(cabFile));
            ArgumentNullException.ThrowIfNull(directoryService, nameof(directoryService));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _directoryService = directoryService;
            _logger = logger;
            _cabFile = cabFile;
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
                _cabFile.FullName,
                TemporaryDirectory.Directory.FullName);

            new CabInfo(_cabFile.FullName).Unpack(TemporaryDirectory.Directory.FullName);

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
                _cabFile.FullName,
                TemporaryDirectory.Directory.FullName);

            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                string destinationFilePath = Path.Combine(temporaryDirectory.Directory.FullName, _cabFile.Name);

                new CabInfo(destinationFilePath).Pack(
                    TemporaryDirectory.Directory.FullName,
                    includeSubdirectories: true,
                    CompressionLevel.Max,
                    progressHandler: null);

                _cabFile.Delete();

                File.Move(destinationFilePath, _cabFile.FullName, overwrite: true);

                _cabFile.Refresh();
            }

            return ValueTask.CompletedTask;
        }
    }
}
