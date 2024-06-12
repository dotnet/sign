// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    internal sealed class SignerSpy : IDataFormatSigner, IDefaultDataFormatSigner
    {
        private readonly List<IDataFormatSigner> _providers;
        private readonly List<FileInfo> _signedFiles = new();

        public IDataFormatSigner Signer { get; }

        internal IReadOnlyList<FileInfo> SignedFiles
        {
            get => _signedFiles;
        }

        internal SignerSpy()
        {
            ISignatureAlgorithmProvider signatureAlgorithmProvider = Mock.Of<ISignatureAlgorithmProvider>();
            ICertificateProvider certificateProvider = Mock.Of<ICertificateProvider>();
            ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();
            IMageCli mageCli = Mock.Of<IMageCli>();
            IManifestSigner manifestSigner = Mock.Of<IManifestSigner>();
            INuGetSignTool nuGetSignTool = Mock.Of<INuGetSignTool>();
            IVsixSignTool openVsixSignTool = Mock.Of<IVsixSignTool>();
            IServiceProvider serviceProvider = Mock.Of<IServiceProvider>();
            IToolConfigurationProvider toolConfigurationProvider = Mock.Of<IToolConfigurationProvider>();
            IFileMatcher fileMatcher = Mock.Of<IFileMatcher>();

            Signer = new AzureSignToolSigner(
                toolConfigurationProvider,
                signatureAlgorithmProvider,
                certificateProvider,
                logger);

            _providers = new List<IDataFormatSigner>()
            {
                new AppInstallerServiceSigner(certificateProvider, logger),
                Signer,
                new ClickOnceSigner(
                    signatureAlgorithmProvider,
                    certificateProvider,
                    serviceProvider,
                    mageCli,
                    manifestSigner,
                    logger,
                    fileMatcher),
                new NuGetSigner(signatureAlgorithmProvider, certificateProvider, nuGetSignTool, logger),
                new VsixSigner(signatureAlgorithmProvider, certificateProvider, openVsixSignTool, logger)
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