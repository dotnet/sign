// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
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
        private readonly static TokenCredential TokenCredential = Mock.Of<TokenCredential>();
        private readonly static Uri KeyVaultUrl = new("https://keyvault.test");
        private const string CertificateName = "a";
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
        public void Constructor_WhenKeyVaultUrlIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultServiceProvider(TokenCredential, keyVaultUrl: null!, CertificateName));

            Assert.Equal("keyVaultUrl", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultServiceProvider(TokenCredential, KeyVaultUrl, certificateName: null!));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new KeyVaultServiceProvider(TokenCredential, KeyVaultUrl, certificateName: string.Empty));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void GetSignatureAlgorithmProvider_WhenServiceProviderIsNull_Throws()
        {
            KeyVaultServiceProvider provider = new(TokenCredential, KeyVaultUrl, CertificateName);

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => provider.GetSignatureAlgorithmProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void GetSignatureAlgorithmProvider_ReturnsSameInstance()
        {
            KeyVaultServiceProvider provider = new(TokenCredential, KeyVaultUrl, CertificateName);

            ConcurrentBag<ISignatureAlgorithmProvider> signatureAlgorithmProviders = [];
            Parallel.For(0, 2, (_, _) =>
            {
                signatureAlgorithmProviders.Add(provider.GetSignatureAlgorithmProvider(serviceProvider));
            });

            Assert.Equal(2, signatureAlgorithmProviders.Count);
            Assert.Same(signatureAlgorithmProviders.First(), signatureAlgorithmProviders.Last());
        }

        [Fact]
        public void GetCertificateProvider_WhenServiceProviderIsNull_Throws()
        {
            KeyVaultServiceProvider provider = new(TokenCredential, KeyVaultUrl, CertificateName);

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => provider.GetSignatureAlgorithmProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void GetCertificateProvider_ReturnsSameInstance()
        {
            KeyVaultServiceProvider provider = new(TokenCredential, KeyVaultUrl, CertificateName);

            ConcurrentBag<ICertificateProvider> certificateProviders = [];
            Parallel.For(0, 2, (_, _) =>
            {
                certificateProviders.Add(provider.GetCertificateProvider(serviceProvider));
            });

            Assert.Equal(2, certificateProviders.Count);
            Assert.Same(certificateProviders.First(), certificateProviders.Last());
        }
    }
}
