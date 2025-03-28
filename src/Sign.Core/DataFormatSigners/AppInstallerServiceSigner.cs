// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    // Not really signing anything, but updates the manifest file with the
    // correct publisher information
    internal sealed class AppInstallerServiceSigner : IDataFormatSigner
    {
        // Windows 10, version 1709.
        internal static readonly XNamespace AppInstaller2017 = XNamespace.Get("http://schemas.microsoft.com/appx/appinstaller/2017");
        // Windows 10, version 1803.
        internal static readonly XNamespace AppInstaller2017_2 = XNamespace.Get("http://schemas.microsoft.com/appx/appinstaller/2017/2");
        // Windows 10, version 1809.
        internal static readonly XNamespace AppInstaller2018 = XNamespace.Get("http://schemas.microsoft.com/appx/appinstaller/2018");
        // Windows version 21H2 build 22000
        internal static readonly XNamespace AppInstaller2021 = XNamespace.Get("http://schemas.microsoft.com/appx/appinstaller/2021");

        private readonly ICertificateProvider _certificateProvider;
        private readonly ILogger<IDataFormatSigner> _logger;

        // Dependency injection requires a public constructor.
        public AppInstallerServiceSigner(
            ICertificateProvider certificateProvider,
            ILogger<IDataFormatSigner> logger)
        {
            ArgumentNullException.ThrowIfNull(certificateProvider, nameof(certificateProvider));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _certificateProvider = certificateProvider;
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

            using (X509Certificate2 certificate = await _certificateProvider.GetCertificateAsync().ConfigureAwait(false))
            {
                // We need to open the files, and update the publisher value
                foreach (FileInfo file in files)
                {
                    XDocument manifest;
                    using (FileStream stream = file.OpenRead())
                    {
                        manifest = XDocument.Load(stream, LoadOptions.PreserveWhitespace);

                        if (TryGetMainElement(manifest, out XElement? mainElement))
                        {
                            string publisher = certificate.SubjectName.Name;

                            mainElement.SetAttributeValue("Publisher", publisher);
                        }
                    }

                    using (FileStream stream = file.Open(FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        manifest.Save(stream);
                    }
                }
            }
        }

        internal static bool TryGetMainElement(XDocument appInstallerManifest, [NotNullWhen(true)] out XElement? mainElement)
        {
            mainElement = null;

            XElement? rootElement = appInstallerManifest.Root;

            if (rootElement is null)
            {
                return false;
            }

            XNamespace[] xmlNamespaces = [AppInstaller2017, AppInstaller2017_2, AppInstaller2018, AppInstaller2021];

            foreach (XNamespace xmlNamespace in xmlNamespaces)
            {
                mainElement = rootElement.Element(xmlNamespace + "MainBundle") ?? rootElement.Element(xmlNamespace + "MainPackage");

                if (mainElement is not null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
