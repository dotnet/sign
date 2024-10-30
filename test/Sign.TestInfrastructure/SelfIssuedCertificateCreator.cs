// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.TestInfrastructure
{
    public static class SelfIssuedCertificateCreator
    {
        public static X509Certificate2 CreateCertificate()
        {
            DateTimeOffset now = DateTimeOffset.Now;

            return CreateCertificate(now.AddMinutes(-5), now.AddMinutes(5));
        }

        public static X509Certificate2 CreateCertificate(DateTimeOffset notBefore, DateTimeOffset notAfter)
        {
            using (RSA keyPair = RSA.Create(keySizeInBits: 3072))
            {
                CertificateRequest request = new(
                    $"CN={Constants.CommonNamePrefix} Certificate ({Guid.NewGuid():D}), O=Organization, L=City, S=State, C=Country",
                    keyPair,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                return request.CreateSelfSigned(notBefore, notAfter);
            }
        }
    }
}
