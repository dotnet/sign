// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class AppxBundleContainer : Container
    {
        private readonly FileInfo _appxBundle;
        private string? _bundleVersion;
        private readonly IDirectoryService _directoryService;
        private readonly ILogger _logger;
        private readonly IMakeAppxCli _makeAppxCli;

        public AppxBundleContainer(
            FileInfo appxBundle,
            IDirectoryService directoryService,
            IFileMatcher fileMatcher,
            IMakeAppxCli makeAppxCli,
            ILogger logger)
            : base(fileMatcher)
        {
            ArgumentNullException.ThrowIfNull(appxBundle, nameof(appxBundle));
            ArgumentNullException.ThrowIfNull(directoryService, nameof(directoryService));
            ArgumentNullException.ThrowIfNull(makeAppxCli, nameof(makeAppxCli));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _appxBundle = appxBundle;
            _directoryService = directoryService;
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

            var args = $@"unbundle /p ""{_appxBundle.FullName}"" /d ""{TemporaryDirectory!.Directory.FullName}"" /o";

            await _makeAppxCli.RunAsync(args);

            _bundleVersion = GetBundleVersion();
        }

        public override async ValueTask SaveAsync()
        {
            if (TemporaryDirectory is null)
            {
                throw new InvalidOperationException();
            }

            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                FileInfo newAppxBundle = new(Path.Combine(temporaryDirectory.Directory.FullName, _appxBundle.Name));

                var args = $@"bundle /d ""{TemporaryDirectory.Directory.FullName}"" /p ""{newAppxBundle.FullName}"" /bv {_bundleVersion} /o";

                await _makeAppxCli.RunAsync(args);

                _appxBundle.Delete();

                File.Move(newAppxBundle.FullName, _appxBundle.FullName, overwrite: true);

                _appxBundle.Refresh();
            }
        }

        private string? GetBundleVersion()
        {
            string fileName = Path.Combine(TemporaryDirectory!.Directory.FullName, "AppxMetadata", "AppxBundleManifest.xml");

            using (FileStream stream = File.OpenRead(fileName))
            {
                XDocument manifest = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
                XNamespace ns = "http://schemas.microsoft.com/appx/2013/bundle";

                return manifest.Root?.Element(ns + "Identity")?.Attribute("Version")?.Value;
            }
        }
    }
}