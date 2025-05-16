// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Azure;
using Azure.CodeSigning;
using Azure.CodeSigning.Models;

namespace Sign.SignatureProviders.TrustedSigning
{
    internal sealed class RSATrustedSigning : RSA
    {
        private readonly CertificateProfileClient _client;
        private readonly string _accountName;
        private readonly string _certificateProfileName;
        private readonly RSA _rsaPublicKey;

        public RSATrustedSigning(
            CertificateProfileClient client,
            string accountName,
            string certificateProfileName,
            RSA rsaPublicKey)
        {
            ArgumentNullException.ThrowIfNull(client, nameof(client));
            ArgumentException.ThrowIfNullOrEmpty(accountName, nameof(accountName));
            ArgumentException.ThrowIfNullOrEmpty(certificateProfileName, nameof(certificateProfileName));
            ArgumentNullException.ThrowIfNull(rsaPublicKey, nameof(rsaPublicKey));

            _client = client;
            _accountName = accountName;
            _certificateProfileName = certificateProfileName;
            _rsaPublicKey = rsaPublicKey;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
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
        {
            SignatureAlgorithm signatureAlgorithm = GetSignatureAlgorithm(hash, padding);
            SignRequest request = new(signatureAlgorithm, hash);
            CertificateProfileSignOperation operation = _client.StartSign(_accountName, _certificateProfileName, request);
            Response<SignStatus> response = operation.WaitForCompletion();
            return response.Value.Signature;
        }

        public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
            => _rsaPublicKey.VerifyHash(hash, signature, hashAlgorithm, padding);

        private static SignatureAlgorithm GetSignatureAlgorithm(byte[] digest, RSASignaturePadding padding)
            => digest.Length switch
            {
                32 => padding == RSASignaturePadding.Pss ? Azure.CodeSigning.Models.SignatureAlgorithm.PS256 : Azure.CodeSigning.Models.SignatureAlgorithm.RS256,
                48 => padding == RSASignaturePadding.Pss ? Azure.CodeSigning.Models.SignatureAlgorithm.PS384 : Azure.CodeSigning.Models.SignatureAlgorithm.RS384,
                64 => padding == RSASignaturePadding.Pss ? Azure.CodeSigning.Models.SignatureAlgorithm.PS512 : Azure.CodeSigning.Models.SignatureAlgorithm.RS512,
                _ => throw new NotSupportedException(),
            };
    }
}
