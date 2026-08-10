// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.SignatureProviders.KeyVault.Test
{
    public class KeyVaultServiceTests
    {
        private const string CertificateName = "a";
        private static readonly ILogger<KeyVaultService> Logger = Substitute.For<ILogger<KeyVaultService>>();

        private readonly CertificateClient _certificateClient = Substitute.For<CertificateClient>();
        private readonly CryptographyClient _cryptographyClient = Substitute.For<CryptographyClient>();

        [Fact]
        public void Constructor_WhenCertificateClientIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(certificateClient: null!, _cryptographyClient, CertificateName, Logger));

            Assert.Equal("certificateClient", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCryptographyClientIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(_certificateClient, cryptographyClient: null!, CertificateName, Logger));

            Assert.Equal("cryptographyClient", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(_certificateClient, _cryptographyClient, certificateName: null!, Logger));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new KeyVaultService(_certificateClient, _cryptographyClient, certificateName: string.Empty, Logger));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(_certificateClient, _cryptographyClient, CertificateName, logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public async Task GetCertificateAsync_CalledTwice_CertificateRetrievedOnce()
        {
            CancellationToken cancellationToken = CancellationToken.None;
            KeyVaultCertificateWithPolicy certificate = CreateKeyVaultCertificateWithPolicy();
            Response<KeyVaultCertificateWithPolicy> response = Response.FromValue(certificate, Substitute.For<Response>());

            _certificateClient
                .GetCertificateAsync(CertificateName, cancellationToken)
                .Returns(response);

            using KeyVaultService service = new(_certificateClient, _cryptographyClient, CertificateName, Logger);

            using X509Certificate2 certificate1 = await service.GetCertificateAsync(cancellationToken);
            using X509Certificate2 certificate2 = await service.GetCertificateAsync(cancellationToken);

            await _certificateClient.Received(1).GetCertificateAsync(CertificateName, cancellationToken);
        }

        [Fact]
        public async Task GetRsaAsync_ReturnsRSAKeyVaultWrapper()
        {
            CancellationToken cancellationToken = CancellationToken.None;
            KeyVaultCertificateWithPolicy certificate = CreateKeyVaultCertificateWithPolicy();
            RSAKeyVault rsaKeyVault = CreateRSAKeyVault();
            Response<KeyVaultCertificateWithPolicy> response = Response.FromValue(certificate, Substitute.For<Response>());

            _certificateClient
                .GetCertificateAsync(CertificateName, cancellationToken)
                .Returns(response);

            _cryptographyClient
                .CreateRSAAsync(cancellationToken)
                .Returns(rsaKeyVault);

            using KeyVaultService service = new(_certificateClient, _cryptographyClient, CertificateName, Logger);

            using RSA rsa = await service.GetRsaAsync(cancellationToken);

            Assert.IsType<RSAKeyVaultWrapper>(rsa);
        }

        private static KeyVaultCertificateWithPolicy CreateKeyVaultCertificateWithPolicy()
        {
            byte[] publicKey = SelfIssuedCertificateCreator.CreateCertificate().Export(X509ContentType.Cert);
            return CertificateModelFactory.KeyVaultCertificateWithPolicy(
                new CertificateProperties("test"),
                cer: publicKey);
        }

        private static RSAKeyVault CreateRSAKeyVault()
        {
#pragma warning disable NS2001 // The Azure SDK grants DynamicProxyGenAssembly2 access to this internal constructor.
            return Substitute.For<RSAKeyVault>(
                Substitute.For<CryptographyClient>(),
                "testId",
                null!);
#pragma warning restore NS2001
        }
    }
}
