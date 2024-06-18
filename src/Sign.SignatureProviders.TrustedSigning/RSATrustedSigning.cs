// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
        private readonly X509Certificate2 _certificate;

        public RSATrustedSigning(
            CertificateProfileClient client,
            string accountName,
            string certificateProfileName,
            X509Certificate2 certificate)
        {
            _client = client;
            _accountName = accountName;
            _certificateProfileName = certificateProfileName;
            _certificate = certificate;
        }

        private RSA PublicKey
            => _certificate.GetRSAPublicKey()!;

        public override RSAParameters ExportParameters(bool includePrivateParameters)
        {
            if (includePrivateParameters)
            {
                throw new NotSupportedException();
            }

            return PublicKey.ExportParameters(false);
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
            => PublicKey.VerifyHash(hash, signature, hashAlgorithm, padding);

        protected override byte[] HashData(byte[] data, int offset, int count, HashAlgorithmName hashAlgorithm)
        {
            using HashAlgorithm hasher = CreateHasher(hashAlgorithm);
            return hasher.ComputeHash(data, offset, count);
        }

        private static SignatureAlgorithm GetSignatureAlgorithm(byte[] digest, RSASignaturePadding padding)
            => digest.Length switch
            {
                32 => padding == RSASignaturePadding.Pss ? Azure.CodeSigning.Models.SignatureAlgorithm.PS256 : Azure.CodeSigning.Models.SignatureAlgorithm.RS256,
                48 => padding == RSASignaturePadding.Pss ? Azure.CodeSigning.Models.SignatureAlgorithm.PS384 : Azure.CodeSigning.Models.SignatureAlgorithm.RS384,
                64 => padding == RSASignaturePadding.Pss ? Azure.CodeSigning.Models.SignatureAlgorithm.PS512 : Azure.CodeSigning.Models.SignatureAlgorithm.RS512,
                _ => throw new NotSupportedException(),
            };

        private static HashAlgorithm CreateHasher(HashAlgorithmName hashAlgorithm)
            => hashAlgorithm.Name switch
            {
                nameof(SHA256) => SHA256.Create(),
                nameof(SHA384) => SHA384.Create(),
                nameof(SHA512) => SHA512.Create(),
                _ => throw new NotSupportedException(),
            };
    }
}
