using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core.Test
{
    [Collection(SigningTestsCollection.Name)]
    public class CertificateSigningContextTests
    {
        private readonly PfxFilesFixture _pfxFilesFixture;

        public CertificateSigningContextTests(PfxFilesFixture pfxFilesFixture)
        {
            ArgumentNullException.ThrowIfNull(pfxFilesFixture, nameof(pfxFilesFixture));

            _pfxFilesFixture = pfxFilesFixture;
        }

        public static IEnumerable<object[]> RsaCertificates
        {
            get
            {
                yield return new object[] { 2048, HashAlgorithmName.SHA256, };
                yield return new object[] { 3072, HashAlgorithmName.SHA384 };
            }
        }

        [Theory]
        [MemberData(nameof(RsaCertificates))]
        public void ShouldSignABlobOfDataWithRsaSha256(int keySizeInBits, HashAlgorithmName hashAlgorithmName)
        {
            using (X509Certificate2 certificate = _pfxFilesFixture.GetPfx(keySizeInBits, hashAlgorithmName))
            using (RSA? privateKey = certificate.GetRSAPrivateKey())
            {
                var config = new SignConfigurationSet
                (
                    publicCertificate: certificate,
                    signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                    fileDigestAlgorithm: HashAlgorithmName.SHA256,
                    signingKey: privateKey!
                );

                var context = new SigningContext(config);
                using (var hash = SHA256.Create())
                {
                    byte[] digest = hash.ComputeHash(new byte[] { 1, 2, 3 });
                    byte[] signature = context.SignDigest(digest);
                    Assert.Equal(OpcKnownUris.SignatureAlgorithms.RsaSHA256, context.XmlDSigIdentifier);
                    Assert.Equal(SigningAlgorithm.RSA, context.SignatureAlgorithm);

                    bool roundtrips = context.VerifyDigest(digest, signature);
                    Assert.True(roundtrips);
                }
            }
        }
    }
}
