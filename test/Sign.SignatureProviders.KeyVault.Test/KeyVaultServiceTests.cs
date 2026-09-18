// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.SignatureProviders.KeyVault.Test
{
    public class KeyVaultServiceTests
    {
        private const string CertificateName = "a";
        private const string CertificateVersion = "0123456789abcdef0123456789abcdef";
        private const string KeyName = "backing-key";
        private const string KeyVersion = "backing-key-version";
        private static readonly Uri VaultUri = new("https://keyvault.test/");
        private static readonly Uri KeyId = new($"https://keyvault.test/keys/{KeyName}/{KeyVersion}");
        private static readonly ILogger<KeyVaultService> Logger = Substitute.For<ILogger<KeyVaultService>>();

        private readonly CertificateClient _certificateClient = Substitute.For<CertificateClient>();
        private readonly CryptographyClient _cryptographyClient = Substitute.For<CryptographyClient>();
        private readonly KeyClient _keyClient = Substitute.For<KeyClient>();

        public KeyVaultServiceTests()
        {
            _certificateClient.VaultUri.Returns(VaultUri);
        }

        [Fact]
        public void Constructor_WhenCertificateClientIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(certificateClient: null!, _keyClient, CertificateName, certificateVersion: null, Logger));

            Assert.Equal("certificateClient", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenKeyClientIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(_certificateClient, keyClient: null!, CertificateName, certificateVersion: null, Logger));

            Assert.Equal("keyClient", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(_certificateClient, _keyClient, certificateName: null!, certificateVersion: null, Logger));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new KeyVaultService(_certificateClient, _keyClient, certificateName: string.Empty, certificateVersion: null, Logger));

            Assert.Equal("certificateName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new KeyVaultService(_certificateClient, _keyClient, CertificateName, certificateVersion: null, logger: null!));

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

            using KeyVaultService service = new(_certificateClient, _keyClient, CertificateName, certificateVersion: null, Logger);

            using X509Certificate2 certificate1 = await service.GetCertificateAsync(cancellationToken);
            using X509Certificate2 certificate2 = await service.GetCertificateAsync(cancellationToken);

            await _certificateClient.Received(1).GetCertificateAsync(CertificateName, cancellationToken);
        }

        [Fact]
        public async Task GetCertificateAsync_WhenCertificateVersionIsSpecified_RetrievesVersion()
        {
            CancellationToken cancellationToken = CancellationToken.None;
            using X509Certificate2 selfIssuedCertificate = SelfIssuedCertificateCreator.CreateCertificate();
            byte[] publicKey = selfIssuedCertificate.Export(X509ContentType.Cert);
            KeyVaultCertificate certificate = CertificateModelFactory.KeyVaultCertificate(
                new CertificateProperties(CertificateName),
                keyId: KeyId,
                cer: publicKey);
            Response<KeyVaultCertificate> response = Response.FromValue(certificate, Substitute.For<Response>());

            _certificateClient
                .GetCertificateVersionAsync(CertificateName, CertificateVersion, cancellationToken)
                .Returns(response);

            using KeyVaultService service = new(
                _certificateClient,
                _keyClient,
                CertificateName,
                CertificateVersion,
                Logger);

            using X509Certificate2 result = await service.GetCertificateAsync(cancellationToken);

            await _certificateClient.Received(1)
                .GetCertificateVersionAsync(CertificateName, CertificateVersion, cancellationToken);
        }

        [Fact]
        public async Task GetCertificateAsync_WhenCertificateVersionDoesNotExist_DoesNotRetrieveLatestVersion()
        {
            CancellationToken cancellationToken = CancellationToken.None;
            RequestFailedException expectedException = new(status: 404, message: "Not found");

            _certificateClient
                .GetCertificateVersionAsync(CertificateName, CertificateVersion, cancellationToken)
                .Returns(Task.FromException<Response<KeyVaultCertificate>>(expectedException));

            using KeyVaultService service = new(
                _certificateClient,
                _keyClient,
                CertificateName,
                CertificateVersion,
                Logger);

            RequestFailedException actualException = await Assert.ThrowsAsync<RequestFailedException>(
                () => service.GetCertificateAsync(cancellationToken));

            Assert.Same(expectedException, actualException);
            await _certificateClient.DidNotReceiveWithAnyArgs()
                .GetCertificateAsync(default!, default);
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
            _keyClient
                .GetCryptographyClient(KeyName, KeyVersion)
                .Returns(_cryptographyClient);

            using KeyVaultService service = new(_certificateClient, _keyClient, CertificateName, certificateVersion: null, Logger);

            using RSA rsa = await service.GetRsaAsync(cancellationToken);

            Assert.IsType<RSAKeyVaultWrapper>(rsa);
            _keyClient.Received(1).GetCryptographyClient(KeyName, KeyVersion);
        }

        [Fact]
        public async Task GetRsaAsync_WhenCertificateAndKeyIdentifiersDiffer_UsesAndCachesBackingKeyIdentifier()
        {
            CancellationToken cancellationToken = CancellationToken.None;
            using X509Certificate2 selfIssuedCertificate = SelfIssuedCertificateCreator.CreateCertificate();
            byte[] publicKey = selfIssuedCertificate.Export(X509ContentType.Cert);
            Uri certificateId = new($"https://keyvault.test/certificates/{CertificateName}/{CertificateVersion}");
            KeyVaultCertificate certificate = CertificateModelFactory.KeyVaultCertificate(
                new CertificateProperties(certificateId),
                keyId: KeyId,
                cer: publicKey);
            Response<KeyVaultCertificate> response = Response.FromValue(certificate, Substitute.For<Response>());
            RSAKeyVault rsaKeyVault = CreateRSAKeyVault();

            _certificateClient
                .GetCertificateVersionAsync(CertificateName, CertificateVersion, cancellationToken)
                .Returns(response);
            _keyClient
                .GetCryptographyClient(KeyName, KeyVersion)
                .Returns(_cryptographyClient);
            _cryptographyClient
                .CreateRSAAsync(cancellationToken)
                .Returns(rsaKeyVault);

            using KeyVaultService service = new(
                _certificateClient,
                _keyClient,
                CertificateName,
                CertificateVersion,
                Logger);

            using RSA rsa1 = await service.GetRsaAsync(cancellationToken);
            using RSA rsa2 = await service.GetRsaAsync(cancellationToken);

            _keyClient.Received(1).GetCryptographyClient(KeyName, KeyVersion);
            _keyClient.DidNotReceive().GetCryptographyClient(CertificateName, CertificateVersion);
        }

        [Theory]
        [InlineData("https://other-keyvault.test/keys/a/key-version")]
        [InlineData("https://keyvault.test/certificates/a/key-version")]
        [InlineData("https://keyvault.test/keys/a")]
        public async Task GetCertificateAsync_WhenKeyIdIsInvalid_Throws(string keyId)
        {
            CancellationToken cancellationToken = CancellationToken.None;
            KeyVaultCertificateWithPolicy certificate = CreateKeyVaultCertificateWithPolicy(
                keyId: new Uri(keyId));
            Response<KeyVaultCertificateWithPolicy> response = Response.FromValue(certificate, Substitute.For<Response>());

            _certificateClient
                .GetCertificateAsync(CertificateName, cancellationToken)
                .Returns(response);

            using KeyVaultService service = new(
                _certificateClient,
                _keyClient,
                CertificateName,
                certificateVersion: null,
                Logger);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetCertificateAsync(cancellationToken));

            Assert.Equal(Resources.InvalidCertificateKeyIdentifier, exception.Message);
            _keyClient.DidNotReceiveWithAnyArgs().GetCryptographyClient(default!, default!);
        }

        [Fact]
        public async Task GetCertificateAsync_WhenKeyIdIsMissing_Throws()
        {
            CancellationToken cancellationToken = CancellationToken.None;
            using X509Certificate2 selfIssuedCertificate = SelfIssuedCertificateCreator.CreateCertificate();
            byte[] publicKey = selfIssuedCertificate.Export(X509ContentType.Cert);
            KeyVaultCertificateWithPolicy certificate = CertificateModelFactory.KeyVaultCertificateWithPolicy(
                new CertificateProperties("test"),
                keyId: null,
                cer: publicKey);
            Response<KeyVaultCertificateWithPolicy> response = Response.FromValue(certificate, Substitute.For<Response>());

            _certificateClient
                .GetCertificateAsync(CertificateName, cancellationToken)
                .Returns(response);

            using KeyVaultService service = new(
                _certificateClient,
                _keyClient,
                CertificateName,
                certificateVersion: null,
                Logger);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetCertificateAsync(cancellationToken));

            Assert.Equal(Resources.InvalidCertificateKeyIdentifier, exception.Message);
            Assert.IsType<ArgumentNullException>(exception.InnerException);
            _keyClient.DidNotReceiveWithAnyArgs().GetCryptographyClient(default!, default!);
        }

        [Fact]
        public async Task GetCertificateAsync_WhenKeyIdIsMalformed_Throws()
        {
            using X509Certificate2 selfIssuedCertificate = SelfIssuedCertificateCreator.CreateCertificate();
            string certificate = Convert.ToBase64String(selfIssuedCertificate.Export(X509ContentType.Cert));
            string responseContent =
                $$"""{"id":"https://keyvault.test/certificates/a/{{CertificateVersion}}","kid":"https://[bad","cer":"{{certificate}}"}""";
            CertificateClientOptions options = new()
            {
                Transport = new HttpClientTransport(new ResponseMessageHandler(responseContent))
            };
            CertificateClient certificateClient = new(VaultUri, new TestTokenCredential(), options);

            using KeyVaultService service = new(
                certificateClient,
                _keyClient,
                CertificateName,
                CertificateVersion,
                Logger);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetCertificateAsync(CancellationToken.None));

            Assert.Equal(Resources.InvalidCertificateKeyIdentifier, exception.Message);
            Assert.IsType<UriFormatException>(exception.InnerException);
            _keyClient.DidNotReceiveWithAnyArgs().GetCryptographyClient(default!, default!);
        }

        private static KeyVaultCertificateWithPolicy CreateKeyVaultCertificateWithPolicy(Uri? keyId = null)
        {
            using X509Certificate2 selfIssuedCertificate = SelfIssuedCertificateCreator.CreateCertificate();
            byte[] publicKey = selfIssuedCertificate.Export(X509ContentType.Cert);
            return CertificateModelFactory.KeyVaultCertificateWithPolicy(
                new CertificateProperties("test"),
                keyId: keyId ?? KeyId,
                cer: publicKey);
        }

        private static RSAKeyVault CreateRSAKeyVault()
        {
            const string keyId = "testId";
            JsonWebKey keyMaterial = null!;

#pragma warning disable NS2001 // The Azure SDK grants DynamicProxyGenAssembly2 access to this internal constructor.
            return Substitute.For<RSAKeyVault>(
                Substitute.For<CryptographyClient>(),
                keyId,
                keyMaterial);
#pragma warning restore NS2001
        }

        private sealed class ResponseMessageHandler : HttpMessageHandler
        {
            private readonly string _content;

            internal ResponseMessageHandler(string content)
            {
                _content = content;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                HttpResponseMessage response = new(HttpStatusCode.OK)
                {
                    Content = new StringContent(_content, Encoding.UTF8, "application/json"),
                    RequestMessage = request
                };

                return Task.FromResult(response);
            }
        }

        private sealed class TestTokenCredential : TokenCredential
        {
            public override AccessToken GetToken(
                TokenRequestContext requestContext,
                CancellationToken cancellationToken)
            {
                return new AccessToken("token", DateTimeOffset.MaxValue);
            }

            public override ValueTask<AccessToken> GetTokenAsync(
                TokenRequestContext requestContext,
                CancellationToken cancellationToken)
            {
                return ValueTask.FromResult(GetToken(requestContext, cancellationToken));
            }
        }
    }
}
