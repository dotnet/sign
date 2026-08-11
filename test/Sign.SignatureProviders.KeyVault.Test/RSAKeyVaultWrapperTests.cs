// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using NSubstitute;

namespace Sign.SignatureProviders.KeyVault.Test
{
    public class RSAKeyVaultWrapperTests
    {
        private readonly RSAKeyVault _rsaKeyVault = CreateRSAKeyVault();
        private readonly RSA _rsaPublicKey = Substitute.For<RSA>();

        [Fact]
        public void Constructor_WhenRSAKeyVaultIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new RSAKeyVaultWrapper(rsaKeyVault: null!, _rsaPublicKey));

            Assert.Equal("rsaKeyVault", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateClientIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new RSAKeyVaultWrapper(_rsaKeyVault, rsaPublicKey: null!));

            Assert.Equal("rsaPublicKey", exception.ParamName);
        }

        [Fact]
        public void Dispose_DisposesRSAKeyVaultAndRSAPublicKey()
        {
            RSAKeyVault rsaKeyVault = CreateRSAKeyVault();
            RecordingRSA rsaPublicKey = new();
            RSAKeyVaultWrapper wrapper = new(rsaKeyVault, rsaPublicKey);
            wrapper.Dispose();

            Assert.Single(
                rsaKeyVault.ReceivedCalls(),
                call =>
                    call.GetMethodInfo().Name == nameof(RSA.Dispose) &&
                    call.GetMethodInfo().GetParameters().Length == 1 &&
                    call.GetMethodInfo().GetParameters()[0].ParameterType == typeof(bool) &&
                    call.GetArguments() is [true]);
            Assert.Equal(1, rsaPublicKey.DisposeTrueCallCount);
        }

        [Fact]
        public void ExportParameters_IncludePrivateParametersIsTrue_Throws()
        {
            using RSAKeyVaultWrapper wrapper = new(_rsaKeyVault, _rsaPublicKey);

            Assert.Throws<NotSupportedException>(
                () => wrapper.ExportParameters(true));
        }

        [Fact]
        public void ExportParameters_IncludePrivateParametersIsFalse_UsesExportParametersOfPublicKey()
        {
            using RSAKeyVaultWrapper wrapper = new(_rsaKeyVault, _rsaPublicKey);

            wrapper.ExportParameters(false);

            _rsaPublicKey.Received(1).ExportParameters(false);
        }

        [Fact]
        public void ImportParameters_Throws()
        {
            using RSAKeyVaultWrapper wrapper = new(_rsaKeyVault, _rsaPublicKey);

            Assert.Throws<NotImplementedException>(
                () => wrapper.ImportParameters(default));
        }

        [Fact]
        public void SignHash_UsesRSAKeyVault()
        {
            using RSAKeyVaultWrapper wrapper = new(_rsaKeyVault, _rsaPublicKey);

            byte[] hash = [];
            HashAlgorithmName hashAlgorithmName = HashAlgorithmName.SHA256;
            RSASignaturePadding padding = RSASignaturePadding.Pkcs1;

            wrapper.SignHash(hash, hashAlgorithmName, padding);

            _rsaKeyVault.Received(1).SignHash(hash, hashAlgorithmName, padding);
        }

        [Fact]
        public void VerifyHash_UsesPublicKey()
        {
            using RSAKeyVaultWrapper wrapper = new(_rsaKeyVault, _rsaPublicKey);

            byte[] hash = [];
            byte[] signature = [];
            HashAlgorithmName hashAlgorithmName = HashAlgorithmName.SHA256;
            RSASignaturePadding padding = RSASignaturePadding.Pkcs1;

            wrapper.VerifyHash(hash, signature, hashAlgorithmName, padding);

            _rsaPublicKey.Received(1).VerifyHash(hash, signature, hashAlgorithmName, padding);
        }

        private static RSAKeyVault CreateRSAKeyVault()
        {
            CryptographyClient client = Substitute.For<CryptographyClient>();
            const string keyId = "testId";
            JsonWebKey keyMaterial = null!;

#pragma warning disable NS2001 // The Azure SDK grants DynamicProxyGenAssembly2 access to this internal constructor.
            return Substitute.For<RSAKeyVault>(client, keyId, keyMaterial);
#pragma warning restore NS2001
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
