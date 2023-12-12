// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    // Unpacking and repacking an appx will strip it of its signature
    // We can also update the publisher of the appxmanifest
    internal sealed class AppxContainer : Container
    {
        private readonly FileInfo _appx;
        private readonly IDirectoryService _directoryService;
        private readonly IKeyVaultService _keyVaultService;
        private readonly ILogger _logger;
        private readonly IMakeAppxCli _makeAppxCli;

        public AppxContainer(
            FileInfo appx,
            IKeyVaultService keyVaultService,
            IDirectoryService directoryService,
            IFileMatcher fileMatcher,
            IMakeAppxCli makeAppxCli,
            ILogger logger)
            : base(fileMatcher)
        {
            ArgumentNullException.ThrowIfNull(appx, nameof(appx));
            ArgumentNullException.ThrowIfNull(keyVaultService, nameof(keyVaultService));
            ArgumentNullException.ThrowIfNull(directoryService, nameof(directoryService));
            ArgumentNullException.ThrowIfNull(makeAppxCli, nameof(makeAppxCli));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _appx = appx;
            _directoryService = directoryService;
            _keyVaultService = keyVaultService;
            _makeAppxCli = makeAppxCli;
            _logger = logger;
        }

        public override async ValueTask OpenAsync()
        {
            if (TemporaryDirectory is not null)
            {
                throw new InvalidOperationException();
            }

            TemporaryDirectory = new TemporaryDirectory(_directoryService);

            _logger.LogInformation(
                Resources.OpeningContainer,
                _appx.FullName,
                TemporaryDirectory.Directory.FullName);

            var args = $@"unpack /p ""{_appx.FullName}"" /d ""{TemporaryDirectory!.Directory.FullName}"" /l /o";

            await _makeAppxCli.RunAsync(args);

            await UpdateManifestPublisherAsync();
        }

        public override async ValueTask SaveAsync()
        {
            if (TemporaryDirectory is null)
            {
                throw new InvalidOperationException();
            }

            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                FileInfo newAppx = new(Path.Combine(temporaryDirectory.Directory.FullName, _appx.Name));

                var args = $@"pack /d ""{TemporaryDirectory!.Directory.FullName}"" /p ""{newAppx.FullName}"" /o /l";

                await _makeAppxCli.RunAsync(args);

                _appx.Delete();

                File.Move(newAppx.FullName, _appx.FullName, overwrite: true);

                _appx.Refresh();
            }
        }

        private async Task UpdateManifestPublisherAsync()
        {
            FileInfo appxManifest = new(Path.Combine(TemporaryDirectory!.Directory.FullName, "AppxManifest.xml"));
            XDocument manifest;

            using (FileStream stream = appxManifest.OpenRead())
            {
                manifest = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
                XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

                XElement? idElement = manifest.Root?.Element(ns + "Identity");

                if (idElement is not null)
                {
                    using (X509Certificate2 certificate = await _keyVaultService.GetCertificateAsync())
                    {
                        string publisher = certificate.SubjectName.Name;

                        idElement.SetAttributeValue("Publisher", publisher);
                    }
                }
            }

            using (FileStream stream = appxManifest.Open(FileMode.Create, FileAccess.Write, FileShare.None))
            {
                manifest.Save(stream);
            }
        }
    }
}