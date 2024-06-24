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

namespace Sign.SignatureProviders.TrustedSigning.Test
{
    public class TrustedSigningServiceProviderTests
    {
        private readonly static TokenCredential TokenCredential = Mock.Of<TokenCredential>();
        private readonly static Uri EndpointUrl = new("https://trustedsigning.test");
        private const string AccountName = "a";
        private const string CertificateProfileName = "b";
        private readonly IServiceProvider serviceProvider;

        public TrustedSigningServiceProviderTests()
        {
            ServiceCollection services = new();
            services.AddSingleton<ILogger<TrustedSigningService>>(new TestLogger<TrustedSigningService>());
            serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void Constructor_WhenTokenCredentialIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningServiceProvider(tokenCredential: null!, EndpointUrl, AccountName, CertificateProfileName));

            Assert.Equal("tokenCredential", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenEndpointUrlIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningServiceProvider(TokenCredential, endpointUrl: null!, AccountName, CertificateProfileName));

            Assert.Equal("endpointUrl", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenAccountNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningServiceProvider(TokenCredential, EndpointUrl, accountName: null!, CertificateProfileName));

            Assert.Equal("accountName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenAccountNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new TrustedSigningServiceProvider(TokenCredential, EndpointUrl, accountName: string.Empty, CertificateProfileName));

            Assert.Equal("accountName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProfileNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningServiceProvider(TokenCredential, EndpointUrl, AccountName, certificateProfileName: null!));

            Assert.Equal("certificateProfileName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProfileNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new TrustedSigningServiceProvider(TokenCredential, EndpointUrl, AccountName, certificateProfileName: string.Empty));

            Assert.Equal("certificateProfileName", exception.ParamName);
        }

        [Fact]
        public void GetSignatureAlgorithmProvider_WhenServiceProviderIsNull_Throws()
        {
            TrustedSigningServiceProvider provider = new(TokenCredential, EndpointUrl, AccountName, CertificateProfileName);

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => provider.GetSignatureAlgorithmProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void GetSignatureAlgorithmProvider_ReturnsSameInstance()
        {
            TrustedSigningServiceProvider provider = new(TokenCredential, EndpointUrl, AccountName, CertificateProfileName);

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
            TrustedSigningServiceProvider provider = new(TokenCredential, EndpointUrl, AccountName, CertificateProfileName);

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => provider.GetSignatureAlgorithmProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void GetCertificateProvider_ReturnsSameInstance()
        {
            TrustedSigningServiceProvider provider = new(TokenCredential, EndpointUrl, AccountName, CertificateProfileName);

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
