// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core
{
    /// <summary>
    /// A configuration set for a signing operation.
    /// </summary>
    internal sealed class SignConfigurationSet
    {
        /// <summary>
        /// Creates a new instance of the <see cref="SignConfigurationSet"/>.
        /// </summary>
        /// <param name="fileDigestAlgorithm">The <see cref="HashAlgorithmName"/> used to digest files.</param>
        /// <param name="signatureDigestAlgorithm">The <see cref="HashAlgorithmName"/> used in signatures.</param>
        /// <param name="signingKey">An <see cref="AsymmetricAlgorithm"/> with a private key that is used to perform signing operations.</param>
        /// <param name="publicCertificate">An <see cref="X509Certificate2"/> that contains the public key and certificate used to embed in the signature.</param>
        public SignConfigurationSet(HashAlgorithmName fileDigestAlgorithm, HashAlgorithmName signatureDigestAlgorithm, AsymmetricAlgorithm signingKey, X509Certificate2 publicCertificate)
        {
            FileDigestAlgorithm = fileDigestAlgorithm;
            SignatureDigestAlgorithm = signatureDigestAlgorithm;
            SigningKey = signingKey;
            PublicCertificate = publicCertificate;
        }

        /// <summary>
        /// The <see cref="HashAlgorithmName"/> used to digest files.
        /// </summary>
        public HashAlgorithmName FileDigestAlgorithm { get; }

        /// <summary>
        /// The <see cref="HashAlgorithmName"/> used in signatures.
        /// </summary>
        public HashAlgorithmName SignatureDigestAlgorithm { get; }

        /// <summary>
        /// An <see cref="AsymmetricAlgorithm"/> with a private key that is used to perform signing operations.
        /// </summary>
        public AsymmetricAlgorithm SigningKey { get; }

        /// <summary>
        /// An <see cref="X509Certificate2"/> that contains the public key and certificate used to embed in the signature.
        /// </summary>
        public X509Certificate2 PublicCertificate { get; }
        
    }
}
