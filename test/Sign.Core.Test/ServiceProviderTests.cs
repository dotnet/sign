// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sign.Core.Test
{
    public class ServiceProviderTests
    {
        [Fact]
        public void Constructor_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ServiceProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void CreateDefault_Always_RegistersRequiredServices()
        {
            ServiceProvider serviceProvider = ServiceProvider.CreateDefault();

            // Start of tests
            Assert.NotNull(serviceProvider.GetRequiredService<ILogger<ServiceProviderTests>>());
            Assert.NotNull(serviceProvider.GetRequiredService<IAppRootDirectoryLocator>());
            Assert.NotNull(serviceProvider.GetRequiredService<IToolConfigurationProvider>());
            Assert.NotNull(serviceProvider.GetRequiredService<IMatcherFactory>());
            Assert.NotNull(serviceProvider.GetRequiredService<IFileListReader>());
            Assert.NotNull(serviceProvider.GetRequiredService<IFileMatcher>());
            Assert.NotNull(serviceProvider.GetRequiredService<IContainerProvider>());
            Assert.NotNull(serviceProvider.GetRequiredService<IFileMetadataService>());
            Assert.NotNull(serviceProvider.GetRequiredService<IDirectoryService>());
            Assert.NotNull(serviceProvider.GetRequiredService<IKeyVaultService>());

            IDefaultSignatureProvider defaultSignatureProvider = serviceProvider.GetRequiredService<IDefaultSignatureProvider>();
            Assert.IsType<AzureSignToolSignatureProvider>(defaultSignatureProvider.SignatureProvider);

            IEnumerable<ISignatureProvider> signatureProviders = serviceProvider.GetServices<ISignatureProvider>();
            Assert.Equal(5, signatureProviders.Count());

            Assert.NotNull(serviceProvider.GetRequiredService<IAggregatingSignatureProvider>());

            Assert.NotNull(serviceProvider.GetRequiredService<IManifestSigner>());
            Assert.NotNull(serviceProvider.GetRequiredService<IMageCli>());
            Assert.NotNull(serviceProvider.GetRequiredService<IMakeAppxCli>());
            Assert.NotNull(serviceProvider.GetRequiredService<INuGetSignTool>());
            Assert.NotNull(serviceProvider.GetRequiredService<IOpenVsixSignTool>());
            Assert.NotNull(serviceProvider.GetRequiredService<ICertificateVerifier>());
            Assert.NotNull(serviceProvider.GetRequiredService<ISigner>());
        }
    }
}