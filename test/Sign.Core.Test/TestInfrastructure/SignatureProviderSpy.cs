// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    internal sealed class SignatureProviderSpy : ISignatureProvider, IDefaultSignatureProvider
    {
        private readonly List<ISignatureProvider> _providers;
        private readonly List<FileInfo> _signedFiles = new();

        public ISignatureProvider SignatureProvider { get; }

        internal IReadOnlyList<FileInfo> SignedFiles
        {
            get => _signedFiles;
        }

        internal SignatureProviderSpy()
        {
            IContainerProvider containerProvider = Mock.Of<IContainerProvider>();
            IDirectoryService directoryService = Mock.Of<IDirectoryService>();
            IKeyVaultService keyVaultService = Mock.Of<IKeyVaultService>();
            ILogger<ISignatureProvider> logger = Mock.Of<ILogger<ISignatureProvider>>();
            IMageCli mageCli = Mock.Of<IMageCli>();
            IManifestSigner manifestSigner = Mock.Of<IManifestSigner>();
            INuGetSignTool nuGetSignTool = Mock.Of<INuGetSignTool>();
            IOpenVsixSignTool openVsixSignTool = Mock.Of<IOpenVsixSignTool>();
            IServiceProvider serviceProvider = Mock.Of<IServiceProvider>();
            IToolConfigurationProvider toolConfigurationProvider = Mock.Of<IToolConfigurationProvider>();

            SignatureProvider = new AzureSignToolSignatureProvider(toolConfigurationProvider, keyVaultService, logger);

            _providers = new List<ISignatureProvider>()
            {
                new AppInstallerServiceSignatureProvider(keyVaultService, logger),
                SignatureProvider,
                new ClickOnceSignatureProvider(
                    keyVaultService,
                    containerProvider,
                    serviceProvider,
                    directoryService,
                    mageCli,
                    manifestSigner,
                    logger),
                new NuGetSignatureProvider(keyVaultService, nuGetSignTool, logger),
                new VsixSignatureProvider(keyVaultService, openVsixSignTool, logger)
            };
        }

        public bool CanSign(FileInfo file)
        {
            return _providers.Any(provider => provider.CanSign(file));
        }

        public Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            _signedFiles.AddRange(files);

            return Task.CompletedTask;
        }
    }
}