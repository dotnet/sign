// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Azure.Core;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.SignatureProviders.TrustedSigning.Test
{
    public class TrustedSigningServiceTests
    {
        private readonly static TokenCredential TokenCredential = Mock.Of<TokenCredential>();
        private readonly static Uri EndpointUrl = new("https://trustedsigning.test");
        private const string AccountName = "a";
        private const string CertificateProfileName = "b";
        private readonly static ILogger<TrustedSigningService> Logger = Mock.Of<ILogger<TrustedSigningService>>();

        [Fact]
        public void Constructor_WhenTokenCredentialIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningService(tokenCredential: null!, EndpointUrl, AccountName, CertificateProfileName, Logger));

            Assert.Equal("tokenCredential", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenEndpointUrlIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningService(TokenCredential, endpointUrl: null!, AccountName, CertificateProfileName, Logger));

            Assert.Equal("endpointUrl", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenAccountNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningService(TokenCredential, EndpointUrl, accountName: null!, CertificateProfileName, Logger));

            Assert.Equal("accountName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenAccountNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new TrustedSigningService(TokenCredential, EndpointUrl, accountName: string.Empty, CertificateProfileName, Logger));

            Assert.Equal("accountName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProfileNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningService(TokenCredential, EndpointUrl, AccountName, certificateProfileName: null!, Logger));

            Assert.Equal("certificateProfileName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProfileNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new TrustedSigningService(TokenCredential, EndpointUrl, AccountName, certificateProfileName: string.Empty, Logger));

            Assert.Equal("certificateProfileName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningService(TokenCredential, EndpointUrl, AccountName, CertificateProfileName, logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }
    }
}
