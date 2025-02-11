// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sign.TestInfrastructure;

namespace Sign.SignatureProviders.KeyVault.Test
{
    public class KeyVaultServiceProviderTests
    {
        private readonly KeyVaultServiceProvider _provider = new();
        private readonly IServiceProvider serviceProvider;

        public KeyVaultServiceProviderTests()
        {
            ServiceCollection services = new();
            services.AddSingleton<ILogger<KeyVaultService>>(new TestLogger<KeyVaultService>());
            services.AddSingleton<KeyVaultService>(sp =>
            {
                return new KeyVaultService(
                    Mock.Of<CertificateClient>(),
                    Mock.Of<CryptographyClient>(),
                    "a", sp.GetRequiredService<ILogger<KeyVaultService>>());
            });
            serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void GetSignatureAlgorithmProvider_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _provider.GetSignatureAlgorithmProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void GetSignatureAlgorithmProvider_WhenServiceProviderIsValid_ReturnsSameInstance()
        {
            KeyVaultServiceProvider provider = new();

            Assert.IsType<KeyVaultService>(_provider.GetSignatureAlgorithmProvider(serviceProvider));
        }

        [Fact]
        public void GetCertificateProvider_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _provider.GetCertificateProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void GetCertificateProvider_WhenServiceProviderIsValid_ReturnsSameInstance()
        {
            Assert.IsType<KeyVaultService>(_provider.GetCertificateProvider(serviceProvider));
        }
    }
}
