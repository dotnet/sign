// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Azure.Security.KeyVault.Keys.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.SignatureProviders.KeyVault
{
    internal sealed class RSAKeyVaultWrapper : RSA
    {
        private readonly RSAKeyVault _rsaKeyVault;
        private readonly RSA _publicKey;

        public RSAKeyVaultWrapper(RSAKeyVault rsaKeyVault, X509Certificate2 certificate)
        {
            _rsaKeyVault = rsaKeyVault;
            _publicKey = certificate.GetRSAPublicKey()!;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _rsaKeyVault.Dispose();
                _publicKey.Dispose();
            }

            base.Dispose(disposing);
        }


        public override RSAParameters ExportParameters(bool includePrivateParameters)
        {
            if (includePrivateParameters)
            {
                throw new NotSupportedException();
            }

            return _publicKey.ExportParameters(false);
        }

        public override void ImportParameters(RSAParameters parameters)
            => throw new NotImplementedException();

        public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
            => _rsaKeyVault.SignHash(hash, hashAlgorithm, padding);

        public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
            => _publicKey.VerifyHash(hash, signature, hashAlgorithm, padding);
    }
}
