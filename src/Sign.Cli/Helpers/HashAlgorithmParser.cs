// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine.Parsing;
using System.Globalization;
using System.Security.Cryptography;

namespace Sign.Cli
{
    internal static class HashAlgorithmParser
    {
        public static HashAlgorithmName ParseHashAlgorithmName(ArgumentResult result)
        {
            if (result.Tokens.Count == 0)
            {
                return HashAlgorithmName.SHA256;
            }

            string token = result.Tokens.Single().Value.ToLowerInvariant();

            switch (token)
            {
                case "sha256":
                    return HashAlgorithmName.SHA256;

                case "sha384":
                    return HashAlgorithmName.SHA384;

                case "sha512":
                    return HashAlgorithmName.SHA512;

                default:
                    result.ErrorMessage = string.Format(CultureInfo.CurrentCulture, Resources.InvalidDigestValue, $"--{result.Argument.Name}");

                    return HashAlgorithmName.SHA256;
            }
        }
    }
}
