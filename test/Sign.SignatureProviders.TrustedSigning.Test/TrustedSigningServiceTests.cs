// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Azure.CodeSigning;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.SignatureProviders.TrustedSigning.Test
{
    public class TrustedSigningServiceTests
    {
        private static readonly CertificateProfileClient CertificateProfileClient = Mock.Of<CertificateProfileClient>();
        private const string AccountName = "a";
        private const string CertificateProfileName = "b";
        private static readonly ILogger<TrustedSigningService> Logger = Mock.Of<ILogger<TrustedSigningService>>();

        [Fact]
        public void Constructor_WhenCertificateProfileClientIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningService(certificateProfileClient: null!, AccountName, CertificateProfileName, Logger));

            Assert.Equal("certificateProfileClient", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenAccountNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningService(CertificateProfileClient, accountName: null!, CertificateProfileName, Logger));

            Assert.Equal("accountName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenAccountNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new TrustedSigningService(CertificateProfileClient, accountName: string.Empty, CertificateProfileName, Logger));

            Assert.Equal("accountName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProfileNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningService(CertificateProfileClient, AccountName, certificateProfileName: null!, Logger));

            Assert.Equal("certificateProfileName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProfileNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new TrustedSigningService(CertificateProfileClient, AccountName, certificateProfileName: string.Empty, Logger));

            Assert.Equal("certificateProfileName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningService(CertificateProfileClient, AccountName, CertificateProfileName, logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }
    }
}
