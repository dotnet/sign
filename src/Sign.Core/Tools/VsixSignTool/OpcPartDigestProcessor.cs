// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;

namespace Sign.Core
{
    internal static class OpcPartDigestProcessor
    {
        public static (byte[] digest, Uri identifier) Digest(OpcPart part, HashAlgorithmName algorithmName)
        {
            var info = new HashAlgorithmInfo(algorithmName);

            using (var hashAlgorithm = info.Create())
            {
                using (var partStream = part.Open())
                {
                    var digest = hashAlgorithm.ComputeHash(partStream);

                    return (digest, info.XmlDSigIdentifier);
                }
            }
        }
    }
}
