// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sign.TestInfrastructure;

namespace Sign.SignatureProviders.KeyVault.Test
{
    public class KeyVaultServiceTests
    {
        private readonly static Uri KeyVaultUrl = new("https://keyvault.test");
        private const string CertificateName = "a";
        private readonly TokenCredential tokenCredential = Mock.Of<TokenCredential>();
        private readonly IServiceProvider serviceProvider;

        public KeyVaultServiceTests()
        {
            ServiceCollection services = new();
            services.AddSingleton<ILogger<KeyVaultService>>(new TestLogger<KeyVaultService>());
            serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void Constructor_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(serviceProvider: null!, tokenCredential, KeyVaultUrl, CertificateName));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenTokenCredentialIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(serviceProvider, tokenCredential: null!, KeyVaultUrl, CertificateName));

            Assert.Equal("tokenCredential", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenTokenKeyVaultUrlIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(serviceProvider, tokenCredential, keyVaultUrl: null!, CertificateName));

            Assert.Equal("keyVaultUrl", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenTokenCertificateNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(serviceProvider, tokenCredential, KeyVaultUrl, certificateName: null!));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenTokenCertificateNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new KeyVaultService(serviceProvider, tokenCredential, KeyVaultUrl, certificateName: string.Empty));

            Assert.Equal("certificateName", exception.ParamName);
        }
    }
}
