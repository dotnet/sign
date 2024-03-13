using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core.Test
{
    public class CertificateSigningContextTests
    {
        private static string CertPath(string str) => Path.Combine(".", "TestAssets", "certs", str);

        public static IEnumerable<object[]> RsaCertificates
        {
            get
            {
                yield return new object[] { CertPath("rsa-2048-Sha256.pfx") };
                yield return new object[] { CertPath("rsa-2048-sha1.pfx") };
            }
        }

        [Theory]
        [MemberData(nameof(RsaCertificates))]
        public void ShouldSignABlobOfDataWithRsaSha256(string pfxPath)
        {
            using (var certificate = new X509Certificate2(pfxPath, "test"))
            {
                var config = new SignConfigurationSet
                (
                    publicCertificate: certificate,
                    signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                    fileDigestAlgorithm: HashAlgorithmName.SHA256,
                    signingKey: certificate.GetRSAPrivateKey()!
                );

                var context = new SigningContext(config);
                using (var hash = SHA256.Create())
                {
                    var digest = hash.ComputeHash(new byte[] { 1, 2, 3 });
                    var signature = context.SignDigest(digest);
                    Assert.Equal(OpcKnownUris.SignatureAlgorithms.RsaSHA256, context.XmlDSigIdentifier);
                    Assert.Equal(SigningAlgorithm.RSA, context.SignatureAlgorithm);

                    var roundtrips = context.VerifyDigest(digest, signature);
                    Assert.True(roundtrips);
                }
            }
        }
    }
}
