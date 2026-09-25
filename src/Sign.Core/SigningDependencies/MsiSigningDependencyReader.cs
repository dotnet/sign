// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using WixToolset.Dtf.WindowsInstaller;
using WixToolset.Dtf.WindowsInstaller.Package;

namespace Sign.Core
{
    internal sealed class MsiSigningDependencyReader : ISigningDependencyReader
    {
        private readonly ILogger<ISigningDependencyReader> _logger;
        private readonly HashSet<string> _extensions;

        // Dependency injection requires a public constructor.
        public MsiSigningDependencyReader(ILogger<ISigningDependencyReader> logger)
        {
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _logger = logger;

            _extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".msi",
                ".msm"
            };
        }

        public bool CanRead(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return _extensions.Contains(file.Extension);
        }

        public IReadOnlyList<FileInfo> GetSigningDependencies(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            if (!file.Exists)
            {
                return Array.Empty<FileInfo>();
            }

            List<FileInfo> dependencies = new();

            try
            {
                using (InstallPackage package = new(file.FullName, DatabaseOpenMode.ReadOnly))
                {
                    foreach (string cabinetName in MsiMedia.GetExternalCabinetNames(package))
                    {
                        dependencies.Add(new FileInfo(Path.Combine(file.DirectoryName!, cabinetName)));
                    }

                    foreach (string filePath in MsiMedia.GetUncompressedFilePaths(package))
                    {
                        dependencies.Add(new FileInfo(Path.Combine(file.DirectoryName!, filePath)));
                    }
                }
            }
            catch (Exception e)
            {
                // The file may not be a well-formed install package.  Treat it as having no
                // dependencies rather than failing the whole run.
                _logger.LogWarning(e, Resources.ReadingSigningDependenciesFailed, file.FullName);

                return Array.Empty<FileInfo>();
            }

            return dependencies;
        }
    }
}
