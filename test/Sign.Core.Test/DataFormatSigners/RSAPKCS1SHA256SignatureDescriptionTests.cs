// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;

namespace Sign.Core.Test
{
    public class RSAPKCS1SHA256SignatureDescriptionTests
    {
        [Fact]
        public void Constructor_Always_InitializesProperties()
        {
            RSAPKCS1SHA256SignatureDescription description = new();

            Assert.Equal(typeof(RSA).AssemblyQualifiedName, description.KeyAlgorithm);
            Assert.Equal(typeof(RSAPKCS1SignatureFormatter).AssemblyQualifiedName, description.FormatterAlgorithm);
            Assert.Equal(typeof(RSAPKCS1SignatureDeformatter).AssemblyQualifiedName, description.DeformatterAlgorithm);
            Assert.Equal(nameof(SHA256), description.DigestAlgorithm);
        }

        [Fact]
        public void CreateDigest_Always_ReturnsSha256()
        {
            RSAPKCS1SHA256SignatureDescription description = new();

            using (HashAlgorithm hashAlgorithm = description.CreateDigest())
            {
                Assert.IsAssignableFrom<SHA256>(hashAlgorithm);
            }
        }
    }
}