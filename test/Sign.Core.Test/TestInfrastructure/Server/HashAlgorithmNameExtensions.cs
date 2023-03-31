// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;

namespace Sign.Core.Test
{
    internal static class HashAlgorithmNameExtensions
    {
        internal static Oid ToOid(this HashAlgorithmName hashAlgorithmName)
        {
            if (hashAlgorithmName == HashAlgorithmName.SHA256)
            {
                return Oids.Sha256;
            }

            if (hashAlgorithmName == HashAlgorithmName.SHA384)
            {
                return Oids.Sha384;
            }

            if (hashAlgorithmName == HashAlgorithmName.SHA512)
            {
                return Oids.Sha512;
            }

            throw new ArgumentException(null, nameof(hashAlgorithmName));
        }
    }
}