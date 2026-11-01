// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;

namespace Sign.Core
{
    internal sealed class TimeStampConfiguration
    {
        public string? Url { get; }
        public HashAlgorithmName DigestAlgorithm { get; }

        public static TimeStampConfiguration None { get; } = new();

        public TimeStampConfiguration(string url, HashAlgorithmName digestAlgorithm)
        {
            Url = url;
            DigestAlgorithm = digestAlgorithm;
        }

        private TimeStampConfiguration()
        {
        }
    }
}
