// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Azure;
using Azure.CodeSigning;
using Azure.CodeSigning.Models;
using NSubstitute;
using Sign.SignatureProviders.ArtifactSigning;

namespace Sign.SignatureProviders.KeyVault.Test
{
    public class RSATrustedSigningTests
    {
        private static readonly string AccountName = "testAccount";
        private static readonly string CertificateProfileName = "testProfile";

        private readonly CertificateProfileClient _client = Substitute.For<CertificateProfileClient>();
        private readonly RSA _rsaPublicKey = Substitute.For<RSA>();

        [Fact]
        public void Constructor_WhenClientIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new RSAArtifactSigning(client: null!, AccountName, CertificateProfileName, _rsaPublicKey));

            Assert.Equal("client", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenAccountNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new RSAArtifactSigning(_client, accountName: null!, CertificateProfileName, _rsaPublicKey));

            Assert.Equal("accountName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenAccountNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new RSAArtifactSigning(_client, accountName: string.Empty, CertificateProfileName, _rsaPublicKey));

            Assert.Equal("accountName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProfileNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new RSAArtifactSigning(_client, AccountName, certificateProfileName: null!, _rsaPublicKey));

            Assert.Equal("certificateProfileName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProfileNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new RSAArtifactSigning(_client, AccountName, certificateProfileName: string.Empty, _rsaPublicKey));

            Assert.Equal("certificateProfileName", exception.ParamName);
        }

        [Fact]
        public void Dispose_DisposesRSAKeyVaultAndRSAPublicKey()
        {
            RecordingRSA rsaPublicKey = new();
            RSAArtifactSigning rsa = new(_client, AccountName, CertificateProfileName, rsaPublicKey);
            rsa.Dispose();

            Assert.Equal(1, rsaPublicKey.DisposeTrueCallCount);
        }

        [Fact]
        public void ExportParameters_IncludePrivateParametersIsTrue_Throws()
        {
            using RSAArtifactSigning rsa = new(_client, AccountName, CertificateProfileName, _rsaPublicKey);

            Assert.Throws<NotSupportedException>(
                () => rsa.ExportParameters(true));
        }

        [Fact]
        public void ExportParameters_IncludePrivateParametersIsFalse_UsesExportParametersOfPublicKey()
        {
            using RSAArtifactSigning rsa = new(_client, AccountName, CertificateProfileName, _rsaPublicKey);

            rsa.ExportParameters(false);

            _rsaPublicKey.Received(1).ExportParameters(false);
        }

        [Fact]
        public void ImportParameters_Throws()
        {
            using RSAArtifactSigning rsa = new(_client, AccountName, CertificateProfileName, _rsaPublicKey);

            Assert.Throws<NotImplementedException>(
                () => rsa.ImportParameters(default));
        }

        [Fact]
        public void SignHash_InvalidHashLength_Throws()
        {
            using RSAArtifactSigning rsa = new(_client, AccountName, CertificateProfileName, _rsaPublicKey);

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
            using RSAArtifactSigning rsa = new(_client, AccountName, CertificateProfileName, _rsaPublicKey);

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
            Response<SignStatus> response = Response.FromValue(
                new SignStatus(Guid.NewGuid(), Status.Succeeded, signature, []),
                Substitute.For<Response>());
            CertificateProfileSignOperation operation = Substitute.For<CertificateProfileSignOperation>();

            operation
                .WaitForCompletion(default)
                .Returns(response);

            _client
                .StartSign(AccountName, CertificateProfileName, Arg.Any<SignRequest>(), null, null, null, default)
                .Returns(operation);

            var result = rsa.SignHash(hash, hashAlgorithmName, padding);

            Assert.Same(signature, result);

            _client.Received(1).StartSign(
                AccountName,
                CertificateProfileName,
                Arg.Is<SignRequest>(request => request != null && request.SignatureAlgorithm == expectedSignatureAlgorithm && ReferenceEquals(request.Digest, hash)),
                null,
                null,
                null,
                default);
        }

        [Fact]
        public void VerifyHash_UsesPublicKey()
        {
            using RSAArtifactSigning rsa = new(_client, AccountName, CertificateProfileName, _rsaPublicKey);

            byte[] hash = [];
            byte[] signature = [];
            HashAlgorithmName hashAlgorithmName = HashAlgorithmName.SHA256;
            RSASignaturePadding padding = RSASignaturePadding.Pkcs1;

            rsa.VerifyHash(hash, signature, hashAlgorithmName, padding);

            _rsaPublicKey.Received(1).VerifyHash(hash, signature, hashAlgorithmName, padding);
        }

        private sealed class RecordingRSA : RSA
        {
            internal int DisposeTrueCallCount { get; private set; }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    DisposeTrueCallCount++;
                }

                base.Dispose(disposing);
            }

            public override RSAParameters ExportParameters(bool includePrivateParameters) => default;

            public override void ImportParameters(RSAParameters parameters)
            {
            }
        }
    }
}
