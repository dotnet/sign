// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    // Not really signing anything, but updates the manifest file with the
    // correct publisher information
    internal sealed class AppInstallerServiceSignatureProvider : ISignatureProvider
    {
        private readonly IKeyVaultService _keyVaultService;
        private readonly ILogger _logger;

        // Dependency injection requires a public constructor.
        public AppInstallerServiceSignatureProvider(
            IKeyVaultService keyVaultService,
            ILogger<ISignatureProvider> logger)
        {
            ArgumentNullException.ThrowIfNull(keyVaultService, nameof(keyVaultService));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _keyVaultService = keyVaultService;
            _logger = logger;
        }

        public bool CanSign(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return string.Equals(file.Extension, ".appinstaller", StringComparison.OrdinalIgnoreCase);
        }

        public async Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            _logger.LogInformation(Resources.EditingAppInstaller, files.Count());

            using (X509Certificate2 certificate = await _keyVaultService.GetCertificateAsync().ConfigureAwait(false))
            {
                // We need to open the files, and update the publisher value
                foreach (FileInfo file in files)
                {
                    XDocument manifest;
                    using (FileStream stream = file.OpenRead())
                    {
                        manifest = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
                        XNamespace ns = "http://schemas.microsoft.com/appx/appinstaller/2017/2";

                        XElement? idElement = manifest.Root?.Element(ns + "MainBundle") ?? manifest.Root?.Element(ns + "MainPackage"); // look for appxbundle tag, if not found check for appx tag
                        string publisher = certificate.SubjectName.Name;
                        idElement?.SetAttributeValue("Publisher", publisher);
                    }

                    using (FileStream stream = file.Open(FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        manifest.Save(stream);
                    }
                }
            }
        }
    }
}