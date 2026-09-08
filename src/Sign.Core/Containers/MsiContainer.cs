// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using WixToolset.Dtf.WindowsInstaller;
using WixToolset.Dtf.WindowsInstaller.Package;

namespace Sign.Core
{
    internal class MsiContainer : Container
    {
        private readonly IDirectoryService _directoryService;
        private readonly ILogger _logger;
        private readonly FileInfo _msiFile;

        internal MsiContainer(
            FileInfo msiFile,
            IDirectoryService directoryService,
            IFileMatcher fileMatcher,
            ILogger logger)
            : base(fileMatcher)
        {
            ArgumentNullException.ThrowIfNull(msiFile, nameof(msiFile));
            ArgumentNullException.ThrowIfNull(directoryService, nameof(directoryService));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _directoryService = directoryService;
            _logger = logger;
            _msiFile = msiFile;
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
                _msiFile.FullName,
                TemporaryDirectory.Directory.FullName);

            using var package = new InstallPackage(
                _msiFile.FullName,
                DatabaseOpenMode.ReadOnly,
                sourceDir: null,
                TemporaryDirectory.Directory.FullName);

            package.ExtractFiles();

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
                _msiFile.FullName,
                TemporaryDirectory.Directory.FullName);

            using (InstallPackage package = new(
                _msiFile.FullName,
                DatabaseOpenMode.Direct,
                sourceDir: null,
                TemporaryDirectory.Directory.FullName))
            {
                package.UpdateFiles();

                // UpdateFiles rebuilds cabinets that aren't embedded in the package into the
                // working directory.  Put them back beside the package before it's thrown away.
                foreach (string cabinetName in MsiMedia.GetExternalCabinetNames(package))
                {
                    string sourceFilePath = Path.Combine(
                        TemporaryDirectory.Directory.FullName,
                        cabinetName);

                    if (!File.Exists(sourceFilePath))
                    {
                        continue;
                    }

                    File.Move(
                        sourceFilePath,
                        Path.Combine(_msiFile.DirectoryName!, cabinetName),
                        overwrite: true);
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
