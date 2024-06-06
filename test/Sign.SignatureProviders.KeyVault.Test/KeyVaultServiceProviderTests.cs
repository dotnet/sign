// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sign.Core;
using Sign.TestInfrastructure;

namespace Sign.SignatureProviders.KeyVault.Test
{
    public class KeyVaultServiceProviderTests
    {
        private readonly static Uri KeyVaultUrl = new("https://keyvault.test");
        private const string CertificateName = "a";
        private readonly TokenCredential tokenCredential = Mock.Of<TokenCredential>();
        private readonly IServiceProvider serviceProvider;

        public KeyVaultServiceProviderTests()
        {
            ServiceCollection services = new();
            services.AddSingleton<ILogger<KeyVaultService>>(new TestLogger<KeyVaultService>());
            serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void Constructor_WhenTokenCredentialIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultServiceProvider(tokenCredential: null!, KeyVaultUrl, CertificateName));

            Assert.Equal("tokenCredential", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenTokenKeyVaultUrlIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultServiceProvider(tokenCredential, keyVaultUrl: null!, CertificateName));

            Assert.Equal("keyVaultUrl", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenTokenCertificateNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultServiceProvider(tokenCredential, KeyVaultUrl, certificateName: null!));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenTokenCertificateNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new KeyVaultServiceProvider(tokenCredential, KeyVaultUrl, certificateName: string.Empty));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void GetSignatureAlgorithmProvider_WhenServiceProviderIsNull_Throws()
        {
            KeyVaultServiceProvider provider = new(tokenCredential, KeyVaultUrl, CertificateName);

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => provider.GetSignatureAlgorithmProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void GetSignatureAlgorithmProvider_ReturnsSameInstance()
        {
            KeyVaultServiceProvider provider = new(tokenCredential, KeyVaultUrl, CertificateName);

            List<ISignatureAlgorithmProvider> signatureAlgorithmProviders = [];
            Parallel.For(0, 2, (_, _) =>
            {
                signatureAlgorithmProviders.Add(provider.GetSignatureAlgorithmProvider(serviceProvider));
            });

            Assert.Equal(2, signatureAlgorithmProviders.Count);
            Assert.Same(signatureAlgorithmProviders[0], signatureAlgorithmProviders[1]);
        }

        [Fact]
        public void GetCertificateProvider_WhenServiceProviderIsNull_Throws()
        {
            KeyVaultServiceProvider provider = new(tokenCredential, KeyVaultUrl, CertificateName);

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => provider.GetSignatureAlgorithmProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void GetCertificateProvider_ReturnsSameInstance()
        {
            KeyVaultServiceProvider provider = new(tokenCredential, KeyVaultUrl, CertificateName);

            List<ICertificateProvider> certificateProviders = [];
            Parallel.For(0, 2, (_, _) =>
            {
                certificateProviders.Add(provider.GetCertificateProvider(serviceProvider));
            });

            Assert.Equal(2, certificateProviders.Count);
            Assert.Same(certificateProviders[0], certificateProviders[1]);
        }
    }
}
