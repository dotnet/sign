// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;

namespace Sign.Core
{
    // This type and its default constructor are public because:
    //   "Algorithms added to CryptoConfig must be accessible from outside their assembly."
    // See https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptoconfig.addalgorithm?view=net-7.0#exceptions
    public sealed class RSAPKCS1SHA256SignatureDescription : RSAPKCS1SignatureDescription
    {
        public RSAPKCS1SHA256SignatureDescription()
            : base("SHA256")
        {
        }

        public sealed override HashAlgorithm CreateDigest()
        {
            return SHA256.Create();
        }
    }
}