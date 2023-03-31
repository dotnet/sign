#pragma warning disable IDE0073
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core.Test
{
    internal static class AttributeUtility
    {
        /// <summary>
        /// Create a signing-certificate-v2 from a certificate.
        /// </summary>
        /// <param name="certificate">The signing certificate.</param>
        /// <param name="hashAlgorithm">The hash algorithm for the signing-certificate-v2 attribute.</param>
        internal static CryptographicAttributeObject CreateSigningCertificateV2Attribute(
            X509Certificate2 certificate,
            HashAlgorithmName hashAlgorithm)
        {
            ArgumentNullException.ThrowIfNull(certificate, nameof(certificate));

            SigningCertificateV2 signingCertificateV2 = SigningCertificateV2.Create(certificate, hashAlgorithm);
            ReadOnlyMemory<byte> bytes = signingCertificateV2.Encode();

            AsnEncodedData data = new(Oids.SigningCertificateV2, bytes.Span);

            return new CryptographicAttributeObject(
                Oids.SigningCertificateV2,
                new AsnEncodedDataCollection(data));
        }
    }
}