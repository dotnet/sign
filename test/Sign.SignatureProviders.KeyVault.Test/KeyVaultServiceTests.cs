// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Azure;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Logging;
using Moq;
using Sign.TestInfrastructure;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.SignatureProviders.KeyVault.Test
{
    public class KeyVaultServiceTests
    {
        private const string CertificateName = "a";
        private static readonly ILogger<KeyVaultService> Logger = Mock.Of<ILogger<KeyVaultService>>();

        private readonly Mock<CertificateClient> _certificateClient = new();
        private readonly Mock<CryptographyClient> _cryptographyClient = new();

        [Fact]
        public void Constructor_WhenCertificateClientIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(certificateClient: null!, _cryptographyClient.Object, CertificateName, Logger));

            Assert.Equal("certificateClient", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCryptographyClientIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(_certificateClient.Object, cryptographyClient: null!, CertificateName, Logger));

            Assert.Equal("cryptographyClient", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(_certificateClient.Object, _cryptographyClient.Object, certificateName: null!, Logger));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new KeyVaultService(_certificateClient.Object, _cryptographyClient.Object, certificateName: string.Empty, Logger));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(_certificateClient.Object, _cryptographyClient.Object, CertificateName, logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public async Task GetCertificateAsync_CalledTwice_CertificateRetrievedOnce()
        {
            CancellationToken cancellationToken = CancellationToken.None;
            Mock<KeyVaultCertificateWithPolicy> certificate = CreateMockKeyVaultCertificateWithPolicy();
            Mock<Response<KeyVaultCertificateWithPolicy>> response = new();

            response
                .Setup(_ => _.Value)
                .Returns(certificate.Object);

            _certificateClient
                .Setup(_ => _.GetCertificateAsync(CertificateName, cancellationToken))
                .ReturnsAsync(response.Object);

            using KeyVaultService service = new(_certificateClient.Object, _cryptographyClient.Object, CertificateName, Logger);

            using X509Certificate2 certificate1 = await service.GetCertificateAsync(cancellationToken);
            using X509Certificate2 certificate2 = await service.GetCertificateAsync(cancellationToken);

            _certificateClient.Verify(_ => _.GetCertificateAsync(CertificateName, cancellationToken), Times.Once);
        }

        [Fact]
        public async Task GetRsaAsync_ReturnsRSAKeyVaultWrapper()
        {
            CancellationToken cancellationToken = CancellationToken.None;
            Mock<KeyVaultCertificateWithPolicy> certificate = CreateMockKeyVaultCertificateWithPolicy();
            Mock<RSAKeyVault> rsaKeyVault = new(Mock.Of<CryptographyClient>(), "testId", null);
            Mock<Response<KeyVaultCertificateWithPolicy>> response = new();

            response
                .Setup(_ => _.Value)
                .Returns(certificate.Object);

            _certificateClient
                .Setup(_ => _.GetCertificateAsync(CertificateName, cancellationToken))
                .ReturnsAsync(response.Object);

            _cryptographyClient
                .Setup(_ => _.CreateRSAAsync(cancellationToken))
                .ReturnsAsync(rsaKeyVault.Object);

            using KeyVaultService service = new(_certificateClient.Object, _cryptographyClient.Object, CertificateName, Logger);

            using RSA rsa = await service.GetRsaAsync(cancellationToken);

            Assert.IsType<RSAKeyVaultWrapper>(rsa);
        }

        private static Mock<KeyVaultCertificateWithPolicy> CreateMockKeyVaultCertificateWithPolicy()
        {
            CertificateProperties certificateProperties = new("test");
            byte[] publicKey = SelfIssuedCertificateCreator.CreateCertificate().Export(X509ContentType.Cert);
            Mock<KeyVaultCertificateWithPolicy> certificate = new(certificateProperties);

            // We need to do this because the property has an internal setter
            typeof(KeyVaultCertificateWithPolicy)
                .GetProperty(nameof(KeyVaultCertificateWithPolicy.Cer))
                ?.GetSetMethod(nonPublic: true)
                ?.Invoke(certificate.Object, [publicKey]);

            return certificate;
        }
    }
}
