// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Azure.CodeSigning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sign.TestInfrastructure;

namespace Sign.SignatureProviders.ArtifactSigning.Test
{
    public class ArtifactSigningServiceProviderTests
    {
        private readonly ArtifactSigningServiceProvider _provider = new();
        private readonly IServiceProvider serviceProvider;

        public ArtifactSigningServiceProviderTests()
        {
            ServiceCollection services = new();
            services.AddSingleton<ILogger<ArtifactSigningService>>(new TestLogger<ArtifactSigningService>());
            services.AddSingleton<ArtifactSigningService>(sp =>
            {
                return new ArtifactSigningService(
                     Mock.Of<CertificateProfileClient>(),
                     "account",
                     "profile",
                     sp.GetRequiredService<ILogger<ArtifactSigningService>>());
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
            Assert.IsType<ArtifactSigningService>(_provider.GetSignatureAlgorithmProvider(serviceProvider));
        }

        [Fact]
        public void GetCertificateProvider_WhenServiceProviderIsValid_ReturnsInstance()
        {
            Assert.IsType<ArtifactSigningService>(_provider.GetCertificateProvider(serviceProvider));
        }
    }
}
