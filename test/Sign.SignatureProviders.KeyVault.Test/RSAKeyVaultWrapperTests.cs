// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Azure.Security.KeyVault.Keys.Cryptography;
using Moq;
using Moq.Protected;

namespace Sign.SignatureProviders.KeyVault.Test
{
    public class RSAKeyVaultWrapperTests
    {
        private readonly Mock<RSAKeyVault> _rsaKeyVault = new(Mock.Of<CryptographyClient>(), "testId", null);
        private readonly Mock<RSA> _rsaPublicKey = new();

        [Fact]
        public void Constructor_WhenRSAKeyVaultIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new RSAKeyVaultWrapper(rsaKeyVault: null!, _rsaPublicKey.Object));

            Assert.Equal("rsaKeyVault", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateClientIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new RSAKeyVaultWrapper(_rsaKeyVault.Object, rsaPublicKey: null!));

            Assert.Equal("rsaPublicKey", exception.ParamName);
        }

        [Fact]
        public void Dispose_DisposesRSAKeyVaultAndRSAPublicKey()
        {
            RSAKeyVaultWrapper wrapper = new(_rsaKeyVault.Object, _rsaPublicKey.Object);
            wrapper.Dispose();

            _rsaKeyVault.Protected().Verify(nameof(RSAKeyVault.Dispose), Times.Once(), [true]);
            _rsaPublicKey.Protected().Verify(nameof(RSA.Dispose), Times.Once(), [true]);
        }

        [Fact]
        public void ExportParameters_IncludePrivateParametersIsTrue_Throws()
        {
            using RSAKeyVaultWrapper wrapper = new(_rsaKeyVault.Object, _rsaPublicKey.Object);

            Assert.Throws<NotSupportedException>(
                () => wrapper.ExportParameters(true));
        }

        [Fact]
        public void ExportParameters_IncludePrivateParametersIsFalse_UsesExportParametersOfPublicKey()
        {
            using RSAKeyVaultWrapper wrapper = new(_rsaKeyVault.Object, _rsaPublicKey.Object);

            wrapper.ExportParameters(false);

            _rsaPublicKey.Verify(_ => _.ExportParameters(false), Times.Once());
        }

        [Fact]
        public void ImportParameters_Throws()
        {
            using RSAKeyVaultWrapper wrapper = new(_rsaKeyVault.Object, _rsaPublicKey.Object);

            Assert.Throws<NotImplementedException>(
                () => wrapper.ImportParameters(default));
        }

        [Fact]
        public void SignHash_UsesRSAKeyVault()
        {
            using RSAKeyVaultWrapper wrapper = new(_rsaKeyVault.Object, _rsaPublicKey.Object);

            byte[] hash = [];
            HashAlgorithmName hashAlgorithmName = HashAlgorithmName.SHA256;
            RSASignaturePadding padding = RSASignaturePadding.Pkcs1;

            wrapper.SignHash(hash, hashAlgorithmName, padding);

            _rsaKeyVault.Verify(_ => _.SignHash(hash, hashAlgorithmName, padding), Times.Once());
        }

        [Fact]
        public void VerifyHash_UsesPublicKey()
        {
            using RSAKeyVaultWrapper wrapper = new(_rsaKeyVault.Object, _rsaPublicKey.Object);

            byte[] hash = [];
            byte[] signature = [];
            HashAlgorithmName hashAlgorithmName = HashAlgorithmName.SHA256;
            RSASignaturePadding padding = RSASignaturePadding.Pkcs1;

            wrapper.VerifyHash(hash, signature, hashAlgorithmName, padding);

            _rsaPublicKey.Verify(_ => _.VerifyHash(hash, signature, hashAlgorithmName, padding), Times.Once());
        }
    }
}
