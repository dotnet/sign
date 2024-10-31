// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Azure.Core;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.SignatureProviders.KeyVault.Test
{
    public class KeyVaultServiceTests
    {
        private readonly static TokenCredential TokenCredential = Mock.Of<TokenCredential>();
        private readonly static Uri KeyVaultUrl = new("https://keyvault.test");
        private const string CertificateName = "a";
        private readonly static ILogger<KeyVaultService> logger = Mock.Of<ILogger<KeyVaultService>>();

        [Fact]
        public void Constructor_WhenTokenCredentialIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(tokenCredential: null!, KeyVaultUrl, CertificateName, null, logger));

            Assert.Equal("tokenCredential", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenKeyVaultUrlIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(TokenCredential, keyVaultUrl: null!, CertificateName, null, logger));

            Assert.Equal("keyVaultUrl", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(TokenCredential, KeyVaultUrl, certificateName: null!, null, logger));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new KeyVaultService(TokenCredential, KeyVaultUrl, certificateName: string.Empty, null, logger));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(TokenCredential, KeyVaultUrl, CertificateName, null, logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }
    }
}
