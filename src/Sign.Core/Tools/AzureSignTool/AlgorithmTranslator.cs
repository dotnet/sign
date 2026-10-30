// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;

namespace Sign.Core
{
    internal static class AlgorithmTranslator
    {
        public static uint HashAlgorithmToAlgId(HashAlgorithmName hashAlgorithmName)
        {
            return hashAlgorithmName.Name switch
            {
                nameof(HashAlgorithmName.SHA256) => 0x0000800c,
                nameof(HashAlgorithmName.SHA384) => 0x0000800d,
                nameof(HashAlgorithmName.SHA512) => 0x0000800e,
                _ => throw new NotSupportedException("The algorithm specified is not supported."),
            };
        }

        public static ReadOnlySpan<byte> HashAlgorithmToOidAsciiTerminated(HashAlgorithmName hashAlgorithmName)
        {
            return hashAlgorithmName.Name switch
            {
                nameof(HashAlgorithmName.SHA256) => "2.16.840.1.101.3.4.2.1\0"u8,
                nameof(HashAlgorithmName.SHA384) => "2.16.840.1.101.3.4.2.2\0"u8,
                nameof(HashAlgorithmName.SHA512) => "2.16.840.1.101.3.4.2.3\0"u8,
                _ => throw new NotSupportedException("The algorithm specified is not supported."),
            };
        }
    }
}
