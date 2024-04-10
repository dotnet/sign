// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;

namespace Sign.Core
{
    internal sealed class HashAlgorithmInfo
    {
        public HashAlgorithmName Name { get; }

        public Uri XmlDSigIdentifier { get; }

        public Oid Oid { get; }

        private Func<HashAlgorithm> Factory { get; }

        public HashAlgorithm Create() => Factory();

        public HashAlgorithmInfo(HashAlgorithmName name)
        {
            Name = name;
            
            if (name == HashAlgorithmName.SHA256)
            {
                XmlDSigIdentifier = OpcKnownUris.HashAlgorithms.Sha256DigestUri;
                Factory = SHA256.Create;
                Oid = new Oid(KnownOids.HashAlgorithms.Sha256);
            }
            else if (name == HashAlgorithmName.SHA384)
            {
                XmlDSigIdentifier = OpcKnownUris.HashAlgorithms.Sha384DigestUri;
                Factory = SHA384.Create;
                Oid = new Oid(KnownOids.HashAlgorithms.Sha384);
            }
            else if (name == HashAlgorithmName.SHA512)
            {
                XmlDSigIdentifier = OpcKnownUris.HashAlgorithms.Sha512DigestUri;
                Factory = SHA512.Create;
                Oid = new Oid(KnownOids.HashAlgorithms.Sha512);
            }
            else
            {
                throw new NotSupportedException(Resources.VSIXSignToolUnsupportedAlgorithm);
            }
        }
    }
}
