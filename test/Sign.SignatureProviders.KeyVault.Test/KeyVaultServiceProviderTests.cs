// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sign.TestInfrastructure;

namespace Sign.SignatureProviders.KeyVault.Test
{
    public class KeyVaultServiceProviderTests
    {
        private readonly IServiceProvider serviceProvider;

        public KeyVaultServiceProviderTests()
        {
            ServiceCollection services = new();
            services.AddSingleton<ILogger<KeyVaultService>>(new TestLogger<KeyVaultService>());
            serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void GetSignatureAlgorithmProvider_WhenServiceProviderIsNull_Throws()
        {
            KeyVaultServiceProvider provider = new();

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => provider.GetSignatureAlgorithmProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void GetSignatureAlgorithmProvider_ReturnsSameInstance()
        {
            KeyVaultServiceProvider provider = new();

            // TODO: Write tests
            //ConcurrentBag<ISignatureAlgorithmProvider> signatureAlgorithmProviders = [];
            //Parallel.For(0, 2, (_, _) =>
            //{
            //    signatureAlgorithmProviders.Add(provider.GetSignatureAlgorithmProvider(serviceProvider));
            //});

            //Assert.Equal(2, signatureAlgorithmProviders.Count);
            //Assert.Same(signatureAlgorithmProviders.First(), signatureAlgorithmProviders.Last());
        }

        [Fact]
        public void GetCertificateProvider_WhenServiceProviderIsNull_Throws()
        {
            KeyVaultServiceProvider provider = new();

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => provider.GetSignatureAlgorithmProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void GetCertificateProvider_ReturnsSameInstance()
        {
            KeyVaultServiceProvider provider = new();

            // TODO: Write tests
            //ConcurrentBag<ICertificateProvider> certificateProviders = [];
            //Parallel.For(0, 2, (_, _) =>
            //{
            //    certificateProviders.Add(provider.GetCertificateProvider(serviceProvider));
            //});

            //Assert.Equal(2, certificateProviders.Count);
            //Assert.Same(certificateProviders.First(), certificateProviders.Last());
        }
    }
}
