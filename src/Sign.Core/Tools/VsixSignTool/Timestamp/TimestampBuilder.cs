// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;

namespace Sign.Core.Timestamp
{
    internal static partial class TimestampBuilder
    {
        public static Task<(TimestampResult, byte[]?)> RequestTimestamp(Uri timestampUri, HashAlgorithmName timestampAlgorithm, TimestampNonce nonce, TimeSpan timeout, byte[] content)
        {
            var info = new HashAlgorithmInfo(timestampAlgorithm);
            byte[] digest;
            using (var hash = info.Create())
            {
                digest = hash.ComputeHash(content);
            }

            return SubmitTimestampRequest(timestampUri, info.Oid, nonce, timeout, digest);
        }
    }
}
