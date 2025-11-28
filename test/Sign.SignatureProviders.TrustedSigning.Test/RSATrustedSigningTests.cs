// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Azure;
using Azure.CodeSigning;
using Azure.CodeSigning.Models;
using Moq;
using Moq.Protected;
using Sign.SignatureProviders.TrustedSigning;

namespace Sign.SignatureProviders.KeyVault.Test
{
    public class RSATrustedSigningTests
    {
        private static readonly string AccountName = "testAccount";
        private static readonly string CertificateProfileName = "testProfile";

        private readonly Mock<CertificateProfileClient> _client = new();
        private readonly Mock<RSA> _rsaPublicKey = new();

        [Fact]
        public void Constructor_WhenClientIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new RSATrustedSigning(client: null!, AccountName, CertificateProfileName, _rsaPublicKey.Object));

            Assert.Equal("client", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenAccountNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new RSATrustedSigning(_client.Object, accountName: null!, CertificateProfileName, _rsaPublicKey.Object));

            Assert.Equal("accountName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenAccountNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new RSATrustedSigning(_client.Object, accountName: string.Empty, CertificateProfileName, _rsaPublicKey.Object));

            Assert.Equal("accountName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProfileNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new RSATrustedSigning(_client.Object, AccountName, certificateProfileName: null!, _rsaPublicKey.Object));

            Assert.Equal("certificateProfileName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProfileNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new RSATrustedSigning(_client.Object, AccountName, certificateProfileName: string.Empty, _rsaPublicKey.Object));

            Assert.Equal("certificateProfileName", exception.ParamName);
        }

        [Fact]
        public void Dispose_DisposesRSAKeyVaultAndRSAPublicKey()
        {
            RSATrustedSigning rsa = new(_client.Object, AccountName, CertificateProfileName, _rsaPublicKey.Object);
            rsa.Dispose();

            _rsaPublicKey.Protected().Verify(nameof(RSA.Dispose), Times.Once(), [true]);
        }

        [Fact]
        public void ExportParameters_IncludePrivateParametersIsTrue_Throws()
        {
            using RSATrustedSigning rsa = new(_client.Object, AccountName, CertificateProfileName, _rsaPublicKey.Object);

            Assert.Throws<NotSupportedException>(
                () => rsa.ExportParameters(true));
        }

        [Fact]
        public void ExportParameters_IncludePrivateParametersIsFalse_UsesExportParametersOfPublicKey()
        {
            using RSATrustedSigning rsa = new(_client.Object, AccountName, CertificateProfileName, _rsaPublicKey.Object);

            rsa.ExportParameters(false);

            _rsaPublicKey.Verify(_ => _.ExportParameters(false), Times.Once());
        }

        [Fact]
        public void ImportParameters_Throws()
        {
            using RSATrustedSigning rsa = new(_client.Object, AccountName, CertificateProfileName, _rsaPublicKey.Object);

            Assert.Throws<NotImplementedException>(
                () => rsa.ImportParameters(default));
        }

        [Fact]
        public void SignHash_InvalidHashLength_Throws()
        {
            using RSATrustedSigning rsa = new(_client.Object, AccountName, CertificateProfileName, _rsaPublicKey.Object);

            byte[] hash = [];
            HashAlgorithmName hashAlgorithmName = HashAlgorithmName.SHA256;
            RSASignaturePadding padding = RSASignaturePadding.Pkcs1;

            Assert.Throws<NotSupportedException>(
                () => rsa.SignHash(hash, hashAlgorithmName, padding));
        }

        [Theory]
        [InlineData(32, nameof(RSASignaturePadding.Pkcs1), nameof(SignatureAlgorithm.RS256))]
        [InlineData(32, nameof(RSASignaturePadding.Pss), nameof(SignatureAlgorithm.PS256))]
        [InlineData(48, nameof(RSASignaturePadding.Pkcs1), nameof(SignatureAlgorithm.RS384))]
        [InlineData(48, nameof(RSASignaturePadding.Pss), nameof(SignatureAlgorithm.PS384))]
        [InlineData(64, nameof(RSASignaturePadding.Pkcs1), nameof(SignatureAlgorithm.RS512))]
        [InlineData(64, nameof(RSASignaturePadding.Pss), nameof(SignatureAlgorithm.PS512))]
        public void SignHash_UsesClient(int hashLength, string paddingName, string expectedSignatureAlgorithmName)
        {
            using RSATrustedSigning rsa = new(_client.Object, AccountName, CertificateProfileName, _rsaPublicKey.Object);

            RSASignaturePadding padding = paddingName switch
            {
                nameof(RSASignaturePadding.Pkcs1) => RSASignaturePadding.Pkcs1,
                nameof(RSASignaturePadding.Pss) => RSASignaturePadding.Pss,
                _ => throw new InvalidOperationException($"Unknown padding name: {paddingName}"),
            };

            SignatureAlgorithm expectedSignatureAlgorithm = expectedSignatureAlgorithmName switch
            {
                nameof(SignatureAlgorithm.RS256) => SignatureAlgorithm.RS256,
                nameof(SignatureAlgorithm.PS256) => SignatureAlgorithm.PS256,
                nameof(SignatureAlgorithm.RS384) => SignatureAlgorithm.RS384,
                nameof(SignatureAlgorithm.PS384) => SignatureAlgorithm.PS384,
                nameof(SignatureAlgorithm.RS512) => SignatureAlgorithm.RS512,
                nameof(SignatureAlgorithm.PS512) => SignatureAlgorithm.PS512,
                _ => throw new InvalidOperationException($"Unknown signature algorithm name: {expectedSignatureAlgorithmName}"),
            };

            byte[] signature = [];
            byte[] hash = new byte[hashLength];
            HashAlgorithmName hashAlgorithmName = HashAlgorithmName.SHA256;
            Mock<Response<SignStatus>> response = new();
            Mock<CertificateProfileSignOperation> operation = new();

            response
                .SetupGet(_ => _.Value)
                    .Returns(new SignStatus(Guid.NewGuid(), Status.Succeeded, signature, []));

            operation
                .Setup(_ => _.WaitForCompletion(default))
                .Returns(response.Object);

            _client.Setup(_ => _.StartSign(AccountName, CertificateProfileName, It.IsAny<SignRequest>(), null, null, null, default))
                    .Returns(operation.Object);

            var result = rsa.SignHash(hash, hashAlgorithmName, padding);

            Assert.Same(signature, result);

            _client.Verify(_ => _.StartSign(
                AccountName,
                CertificateProfileName,
                It.Is<SignRequest>(request => request.SignatureAlgorithm == expectedSignatureAlgorithm && ReferenceEquals(request.Digest, hash)),
                null,
                null,
                null,
                default), Times.Once());
        }

        [Fact]
        public void VerifyHash_UsesPublicKey()
        {
            using RSATrustedSigning rsa = new(_client.Object, AccountName, CertificateProfileName, _rsaPublicKey.Object);

            byte[] hash = [];
            byte[] signature = [];
            HashAlgorithmName hashAlgorithmName = HashAlgorithmName.SHA256;
            RSASignaturePadding padding = RSASignaturePadding.Pkcs1;

            rsa.VerifyHash(hash, signature, hashAlgorithmName, padding);

            _rsaPublicKey.Verify(_ => _.VerifyHash(hash, signature, hashAlgorithmName, padding), Times.Once());
        }
    }
}
