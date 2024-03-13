// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal class OpcPartDigest
    {
        public Uri ReferenceUri { get; }
        public Uri DigestAlgorithmIdentifier { get; }
        public byte[] Digest { get; }

        public OpcPartDigest(Uri referenceUri, Uri digestAlgorithmIdentifer, byte[] digest)
        {
            ReferenceUri = referenceUri;
            DigestAlgorithmIdentifier = digestAlgorithmIdentifer;
            Digest = digest;
        }
    }

}
