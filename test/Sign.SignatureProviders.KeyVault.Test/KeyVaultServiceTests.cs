// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys.Cryptography;

using Microsoft.Extensions.Logging;

using Moq;

namespace Sign.SignatureProviders.KeyVault.Test
{
    public class KeyVaultServiceTests
    {
        private static readonly CertificateClient CertificateClient = Mock.Of<CertificateClient>();
        private static readonly CryptographyClient CryptographyClient = Mock.Of<CryptographyClient>();
        private const string CertificateName = "a";
        private static readonly ILogger<KeyVaultService> logger = Mock.Of<ILogger<KeyVaultService>>();

        [Fact]
        public void Constructor_WhenCertificateClientIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(certificateClient: null!, CryptographyClient, CertificateName, logger));

            Assert.Equal("certificateClient", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCryptographyClientIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(CertificateClient, cryptographyClient: null!, CertificateName, logger));

            Assert.Equal("cryptographyClient", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(CertificateClient, CryptographyClient, certificateName: null!, logger));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new KeyVaultService(CertificateClient, CryptographyClient, certificateName: string.Empty, logger));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(CertificateClient, CryptographyClient, CertificateName, logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }
    }
}
