// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;

namespace Sign.Core
{
    internal static class SignatureAlgorithmTranslator
    {
        public static Uri SignatureAlgorithmToXmlDSigUri(SigningAlgorithm signatureAlgorithm, HashAlgorithmName hashAlgorithmName)
        {
            switch (signatureAlgorithm)
            {
                case SigningAlgorithm.RSA when hashAlgorithmName.Name == HashAlgorithmName.SHA256.Name:
                    return OpcKnownUris.SignatureAlgorithms.RsaSHA256;
                case SigningAlgorithm.RSA when hashAlgorithmName.Name == HashAlgorithmName.SHA384.Name:
                    return OpcKnownUris.SignatureAlgorithms.RsaSHA384;
                case SigningAlgorithm.RSA when hashAlgorithmName.Name == HashAlgorithmName.SHA512.Name:
                    return OpcKnownUris.SignatureAlgorithms.RsaSHA512;
                default:
                    throw new NotSupportedException("The algorithm specified is not supported.");

            }
        }
    }
}
