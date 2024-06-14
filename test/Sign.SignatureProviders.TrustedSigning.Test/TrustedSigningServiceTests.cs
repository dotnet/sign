// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sign.TestInfrastructure;

namespace Sign.SignatureProviders.TrustedSigning.Test
{
    public class TrustedSigningServiceTests
    {
        private readonly static TokenCredential TokenCredential = Mock.Of<TokenCredential>();
        private readonly static Uri EndpointUrl = new("https://trustedsigning.test");
        private const string AccountName = "a";
        private const string CertificateProfileName = "b";
        private readonly IServiceProvider serviceProvider;

        public TrustedSigningServiceTests()
        {
            ServiceCollection services = new();
            services.AddSingleton<ILogger<TrustedSigningService>>(new TestLogger<TrustedSigningService>());
            serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void Constructor_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningService(serviceProvider: null!, TokenCredential, EndpointUrl, AccountName, CertificateProfileName));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenTokenCredentialIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningService(serviceProvider, tokenCredential: null!, EndpointUrl, AccountName, CertificateProfileName));

            Assert.Equal("tokenCredential", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenEndpointUrlIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningService(serviceProvider, TokenCredential, endpointUrl: null!, AccountName, CertificateProfileName));

            Assert.Equal("endpointUrl", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenAccountNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningService(serviceProvider, TokenCredential, EndpointUrl, accountName: null!, CertificateProfileName));

            Assert.Equal("accountName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenAccountNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new TrustedSigningService(serviceProvider, TokenCredential, EndpointUrl, accountName: string.Empty, CertificateProfileName));

            Assert.Equal("accountName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProfileNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningService(serviceProvider, TokenCredential, EndpointUrl, AccountName, certificateProfileName: null!));

            Assert.Equal("certificateProfileName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProfileNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new TrustedSigningService(serviceProvider, TokenCredential, EndpointUrl, AccountName, certificateProfileName: string.Empty));

            Assert.Equal("certificateProfileName", exception.ParamName);
        }
    }
}
