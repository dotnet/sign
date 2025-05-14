// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Azure.Security.KeyVault.Keys.Cryptography;

namespace Sign.SignatureProviders.KeyVault
{
    internal sealed class RSAKeyVaultWrapper : RSA
    {
        private readonly RSAKeyVault _rsaKeyVault;
        private readonly RSA _rsaPublicKey;

        public RSAKeyVaultWrapper(RSAKeyVault rsaKeyVault, RSA rsaPublicKey)
        {
            ArgumentNullException.ThrowIfNull(rsaKeyVault, nameof(rsaKeyVault));
            ArgumentNullException.ThrowIfNull(rsaPublicKey, nameof(rsaPublicKey));

            _rsaKeyVault = rsaKeyVault;
            _rsaPublicKey = rsaPublicKey;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _rsaKeyVault.Dispose();
                _rsaPublicKey.Dispose();
            }

            base.Dispose(disposing);
        }


        public override RSAParameters ExportParameters(bool includePrivateParameters)
        {
            if (includePrivateParameters)
            {
                throw new NotSupportedException();
            }

            return _rsaPublicKey.ExportParameters(false);
        }

        public override void ImportParameters(RSAParameters parameters)
            => throw new NotImplementedException();

        public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
            => _rsaKeyVault.SignHash(hash, hashAlgorithm, padding);

        public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
            => _rsaPublicKey.VerifyHash(hash, signature, hashAlgorithm, padding);
    }
}
