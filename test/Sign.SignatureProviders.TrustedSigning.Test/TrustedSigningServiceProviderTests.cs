// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sign.TestInfrastructure;

namespace Sign.SignatureProviders.TrustedSigning.Test
{
    public class TrustedSigningServiceProviderTests
    {
        private readonly IServiceProvider serviceProvider;

        public TrustedSigningServiceProviderTests()
        {
            ServiceCollection services = new();
            services.AddSingleton<ILogger<TrustedSigningService>>(new TestLogger<TrustedSigningService>());
            serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void GetSignatureAlgorithmProvider_WhenServiceProviderIsNull_Throws()
        {
            TrustedSigningServiceProvider provider = new();

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => provider.GetSignatureAlgorithmProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }


        [Fact]
        public void GetSignatureAlgorithmProvider_ReturnsInstance()
        {
            TrustedSigningServiceProvider provider = new();

            // TODO: Not sure how to test this without creating a CertificateProfileClient
            //Assert.IsAssignableFrom<TrustedSigningService>(provider.GetSignatureAlgorithmProvider(serviceProvider));
            //Assert.IsAssignableFrom<TrustedSigningService>(provider.GetSignatureAlgorithmProvider(serviceProvider));
        }

        [Fact]
        public void GetCertificateProvider_ReturnsInstance()
        {
            TrustedSigningServiceProvider provider = new();

            // TODO: Not sure how to test this without creating a CertificateProfileClient
            //Assert.IsAssignableFrom<TrustedSigningService>(provider.GetSignatureAlgorithmProvider(serviceProvider));
        }
    }
}
