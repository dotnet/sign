// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core
{
    /// <summary>
    /// An interface for a signing context. Implementors of this interface will be able to
    /// sign a VSIX package.
    /// </summary>
    internal interface ISigningContext
    {
        /// <summary>
        /// Gets the date and time that the context was created.
        /// Implementers 
        /// </summary>
        DateTimeOffset ContextCreationTime { get; }

        /// <summary>
        /// Gets the digest algorithm that is used to compute the digest to sign.
        /// </summary>
        HashAlgorithmName FileDigestAlgorithmName { get; }

        /// <summary>
        /// Gets the public certificate that the public private key pair belong to.
        /// </summary>
        X509Certificate2 Certificate { get; }

        /// <summary>
        /// Gets the signing algorithm used to compute signatures and perform validations.
        /// </summary>
        SigningAlgorithm SignatureAlgorithm { get; }

        /// <summary>
        /// Gets the XmlDSig signature identifier used to perform the signature opertation.
        /// </summary>
        Uri XmlDSigIdentifier { get; }

        /// <summary>
        /// Signs a digest.
        /// </summary>
        /// <param name="digest">The digest to sign. This must be digested with the same algorithm as identified in the <see cref="FileDigestAlgorithmName"/>.</param>
        /// <returns>The signed digest.</returns>
        byte[] SignDigest(byte[] digest);

        /// <summary>
        /// Verifies a digest.
        /// </summary>
        /// <param name="digest">The digest to verify. This must be digested with the same algorithm as identified in the <see cref="FileDigestAlgorithmName"/>.</param>
        /// <param name="signature">The signature of the digest to perform validation with.</param>
        /// <returns>True if the digest is valid, otherwise false.</returns>
        bool VerifyDigest(byte[] digest, byte[] signature);
    }
}