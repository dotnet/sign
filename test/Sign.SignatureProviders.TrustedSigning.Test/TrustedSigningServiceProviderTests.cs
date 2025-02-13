// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Azure.CodeSigning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sign.TestInfrastructure;

namespace Sign.SignatureProviders.TrustedSigning.Test
{
    public class TrustedSigningServiceProviderTests
    {
        private readonly TrustedSigningServiceProvider _provider = new();
        private readonly IServiceProvider serviceProvider;

        public TrustedSigningServiceProviderTests()
        {
            ServiceCollection services = new();
            services.AddSingleton<ILogger<TrustedSigningService>>(new TestLogger<TrustedSigningService>());
            services.AddSingleton<TrustedSigningService>(sp =>
            {
                return new TrustedSigningService(
                     Mock.Of<CertificateProfileClient>(),
                     "account",
                     "profile",
                     sp.GetRequiredService<ILogger<TrustedSigningService>>());
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
        public void GetSignatureAlgorithmProvider_WhenServiceProviderIsValid_ReturnsInstance()
        {
            Assert.IsType<TrustedSigningService>(_provider.GetSignatureAlgorithmProvider(serviceProvider));
        }

        [Fact]
        public void GetCertificateProvider_WhenServiceProviderIsValid_ReturnsInstance()
        {
            Assert.IsType<TrustedSigningService>(_provider.GetCertificateProvider(serviceProvider));
        }
    }
}
