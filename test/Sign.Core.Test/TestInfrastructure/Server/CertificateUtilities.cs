// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core.Test
{
    internal static class CertificateUtilities
    {
        internal static RSA CreateKeyPair(int strength = 2048)
        {
            return RSA.Create(keySizeInBits: strength);
        }

        internal static X509Certificate2 GetCertificateWithPrivateKey(X509Certificate2 certificate, AsymmetricAlgorithm keyPair)
        {
            if (keyPair is RSA rsa)
            {
                return certificate.CopyWithPrivateKey(rsa);
            }

            if (keyPair is ECDsa ecdsa)
            {
                return certificate.CopyWithPrivateKey(ecdsa);
            }

            throw new ArgumentException(message: null, nameof(keyPair));
        }

        internal static ReadOnlyMemory<byte> Hash(this X509Certificate2 certificate, HashAlgorithmName hashAlgorithmName)
        {
            ArgumentNullException.ThrowIfNull(certificate, nameof(certificate));

            using (HashAlgorithm hashAlgorithm = Create(hashAlgorithmName))
            {
                return hashAlgorithm.ComputeHash(certificate.RawData);
            }
        }

        // It would seem that calling HashAlgorithm.Create(string) would be the simplest option,
        // but it generates the following linker warning when trimming is enabled:
        //      Trim analysis warning IL2026:
        //      Microsoft.VisualStudio.Extensions.Signing.HashingExtensions.<HashAsync>d__1.MoveNext():
        //      Using member 'System.Security.Cryptography.HashAlgorithm.Create(String)'
        //      which has 'RequiresUnreferencedCodeAttribute' can break functionality
        //      when trimming application code. The default algorithm implementations
        //      might be removed, use strong type references like 'RSA.Create()' instead.
        // Ignoring the warning and using HashAlgorithm.Create(string) anyway results in runtime
        // error calling that method.
        private static HashAlgorithm Create(HashAlgorithmName hashAlgorithmName)
        {
            if (hashAlgorithmName == HashAlgorithmName.SHA256)
            {
                return SHA256.Create();
            }
            else if (hashAlgorithmName == HashAlgorithmName.SHA384)
            {
                return SHA384.Create();
            }
            else if (hashAlgorithmName == HashAlgorithmName.SHA512)
            {
                return SHA512.Create();
            }

            throw new ArgumentException(message: null, nameof(hashAlgorithmName));
        }
    }
}