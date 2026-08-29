// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using Sign.Core.Timestamp;
using Xunit.Abstractions;

namespace Sign.Core.Test
{
    [Collection(SigningTestsCollection.Name)]
    public sealed class OpcPackageSigningTests : IDisposable
    {
        private static readonly string SamplePackage = Path.Combine(".", "TestAssets", "VSIXSamples", "OpenVsixSignToolTest.vsix");
        private readonly List<string> _shadowFiles = new List<string>();

        private readonly CertificatesFixture _certificatesFixture;
        private readonly PfxFilesFixture _pfxFilesFixture;
        private readonly ITestOutputHelper _testOutputHelper;

        public OpcPackageSigningTests(
            CertificatesFixture certificatesFixture,
            PfxFilesFixture pfxFilesFixture,
            ITestOutputHelper testOutputHelper)
        {
            ArgumentNullException.ThrowIfNull(certificatesFixture, nameof(certificatesFixture));
            ArgumentNullException.ThrowIfNull(pfxFilesFixture, nameof(pfxFilesFixture));
            ArgumentNullException.ThrowIfNull(testOutputHelper, nameof(testOutputHelper));

            _certificatesFixture = certificatesFixture;
            _pfxFilesFixture = pfxFilesFixture;
            _testOutputHelper = testOutputHelper;
        }

        [Theory]
        [MemberData(nameof(RsaSigningTheories))]
        public void ShouldSignFileWithRsa(int keySizeInBits, HashAlgorithmName hashAlgorithmName, HashAlgorithmName fileDigestAlgorithm, string expectedAlgorithm)
        {
            _ = expectedAlgorithm;

            using (OpcPackage package = ShadowCopyPackage(SamplePackage, out string path, OpcPackageFileMode.ReadWrite))
            {
                OpcPackageSignatureBuilder builder = package.CreateSignatureBuilder();
                builder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();

                using (X509Certificate2 certificate = _pfxFilesFixture.GetPfx(keySizeInBits, hashAlgorithmName))
                using (RSA? rsaPrivateKey = certificate.GetRSAPrivateKey())
                {
                    OpcSignature result = builder.Sign(
                        new SignConfigurationSet(
                            publicCertificate: certificate,
                            signatureDigestAlgorithm: fileDigestAlgorithm,
                            fileDigestAlgorithm: fileDigestAlgorithm,
                            signingKey: rsaPrivateKey!));
                    Assert.NotNull(result);
                }
            }
        }

        public static IEnumerable<object[]> RsaSigningTheories
        {
            get
            {
                yield return new object[] { 2048, HashAlgorithmName.SHA256, HashAlgorithmName.SHA512, OpcKnownUris.SignatureAlgorithms.RsaSHA512.AbsoluteUri };
                yield return new object[] { 2048, HashAlgorithmName.SHA256, HashAlgorithmName.SHA384, OpcKnownUris.SignatureAlgorithms.RsaSHA384.AbsoluteUri };
                yield return new object[] { 2048, HashAlgorithmName.SHA256, HashAlgorithmName.SHA256, OpcKnownUris.SignatureAlgorithms.RsaSHA256.AbsoluteUri };
            }
        }

        [Theory]
        [MemberData(nameof(RsaTimestampTheories))]
        public async Task ShouldTimestampFileWithRsa(int keySizeInBits, HashAlgorithmName hashAlgorithmName, HashAlgorithmName timestampDigestAlgorithm)
        {
            using (OpcPackage package = ShadowCopyPackage(SamplePackage, out var path, OpcPackageFileMode.ReadWrite))
            {
                OpcPackageSignatureBuilder signerBuilder = package.CreateSignatureBuilder();
                signerBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();

                using (X509Certificate2 certificate = _pfxFilesFixture.GetPfx(keySizeInBits, hashAlgorithmName))
                using (RSA? rsaPrivateKey = certificate.GetRSAPrivateKey())
                {
                    OpcSignature signature = signerBuilder.Sign(
                        new SignConfigurationSet(
                            publicCertificate: certificate,
                            signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                            fileDigestAlgorithm: HashAlgorithmName.SHA256,
                            signingKey: rsaPrivateKey!));
                    OpcPackageTimestampBuilder timestampBuilder = signature.CreateTimestampBuilder();
                    TimestampResult result = await timestampBuilder.SignAsync(_certificatesFixture.TimestampServiceUrl, timestampDigestAlgorithm);

                    Assert.Equal(TimestampResult.Success, result);
                }
            }
        }

        [Fact]
        public void ShouldSignPartWithOctothorpeInName()
        {
            // Regression test for https://github.com/dotnet/sign/issues/998: a part whose
            // name contains '#' must not have its manifest Reference URI silently truncated
            // at the '#' (which System.Uri parses as a fragment delimiter unless escaped).
            const string specialPartName = "ab#c.txt";
            const string specialPartContents = "content with an octothorpe in the part name";

            string temp = Path.GetTempFileName();
            _shadowFiles.Add(temp);
            File.Copy(SamplePackage, temp, overwrite: true);

            using (ZipArchive archive = ZipFile.Open(temp, ZipArchiveMode.Update))
            {
                ZipArchiveEntry entry = archive.CreateEntry(specialPartName);
                using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
                writer.Write(specialPartContents);
            }

            byte[] expectedDigest;
            using (SHA256 sha256 = SHA256.Create())
            {
                expectedDigest = sha256.ComputeHash(Encoding.UTF8.GetBytes(specialPartContents));
            }

            string? referenceUri = null;
            string? digestValueBase64 = null;

            using (OpcPackage package = OpcPackage.Open(temp, OpcPackageFileMode.ReadWrite))
            {
                OpcPart? specialPart = package.GetParts().FirstOrDefault(p => p.Uri.ToPackagePath() == specialPartName);

                Assert.NotNull(specialPart);
                Assert.Equal(string.Empty, specialPart!.Uri.Fragment);

                OpcPackageSignatureBuilder signerBuilder = package.CreateSignatureBuilder();
                signerBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();

                using (X509Certificate2 certificate = _pfxFilesFixture.GetPfx(keySizeInBits: 2048, HashAlgorithmName.SHA256))
                using (RSA? rsaPrivateKey = certificate.GetRSAPrivateKey())
                {
                    OpcSignature signature = signerBuilder.Sign(
                        new SignConfigurationSet(
                            publicCertificate: certificate,
                            signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                            fileDigestAlgorithm: HashAlgorithmName.SHA256,
                            signingKey: rsaPrivateKey!));

                    using Stream sigStream = signature.Part!.Open();
                    var document = new XmlDocument();
                    document.Load(sigStream);

                    var nsmgr = new XmlNamespaceManager(document.NameTable);
                    nsmgr.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");

                    XmlNode? reference = document.SelectSingleNode("//ds:Reference[contains(@URI, 'ab')]", nsmgr);

                    Assert.NotNull(reference);
                    referenceUri = reference!.Attributes?["URI"]?.Value;
                    digestValueBase64 = reference.SelectSingleNode("ds:DigestValue", nsmgr)?.InnerText;
                }
            }

            Assert.NotNull(referenceUri);
            Assert.DoesNotContain('#', referenceUri);
            Assert.Contains("%23", referenceUri, StringComparison.OrdinalIgnoreCase);

            byte[] actualDigest = Convert.FromBase64String(digestValueBase64 ?? string.Empty);
            Assert.Equal(expectedDigest, actualDigest);
        }

        [Fact]
        public void ShouldSupportReSigning()
        {
            string path;
            using (X509Certificate2 certificate = _pfxFilesFixture.GetPfx(keySizeInBits: 2048, HashAlgorithmName.SHA256))
            using (RSA? rsaPrivateKey = certificate.GetRSAPrivateKey())
            {
                using (OpcPackage package = ShadowCopyPackage(SamplePackage, out path, OpcPackageFileMode.ReadWrite))
                {
                    OpcPackageSignatureBuilder signerBuilder = package.CreateSignatureBuilder();
                    signerBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();
                    signerBuilder.Sign(
                        new SignConfigurationSet(
                            publicCertificate: certificate,
                            signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                            fileDigestAlgorithm: HashAlgorithmName.SHA256,
                            signingKey: rsaPrivateKey!));
                }
                using (OpcPackage package = OpcPackage.Open(path, OpcPackageFileMode.ReadWrite))
                {
                    OpcPackageSignatureBuilder signerBuilder = package.CreateSignatureBuilder();
                    signerBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();
                    signerBuilder.Sign(
                        new SignConfigurationSet(
                            publicCertificate: certificate,
                            signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                            fileDigestAlgorithm: HashAlgorithmName.SHA256,
                            signingKey: rsaPrivateKey!));
                }
            }
            using (OpcPackage netfxPackage = OpcPackage.Open(path))
            {
                Assert.NotEmpty(netfxPackage.GetSignatures());
            }
        }

        [Fact]
        public void ShouldSupportReSigningWithDifferentCertificate()
        {
            string path;

            using (X509Certificate2 certificate = _pfxFilesFixture.GetPfx(keySizeInBits: 2048, HashAlgorithmName.SHA256))
            using (RSA? rsaPrivateKey = certificate.GetRSAPrivateKey())
            using (OpcPackage package = ShadowCopyPackage(SamplePackage, out path, OpcPackageFileMode.ReadWrite))
            {
                OpcPackageSignatureBuilder signerBuilder = package.CreateSignatureBuilder();
                signerBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();
                signerBuilder.Sign(
                    new SignConfigurationSet(
                        publicCertificate: certificate,
                        signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                        fileDigestAlgorithm: HashAlgorithmName.SHA256,
                        signingKey: rsaPrivateKey!));
            }

            using (X509Certificate2 certificate = _pfxFilesFixture.GetPfx(keySizeInBits: 3072, HashAlgorithmName.SHA384))
            using (RSA? rsaPrivateKey = certificate.GetRSAPrivateKey())
            using (OpcPackage package = OpcPackage.Open(path, OpcPackageFileMode.ReadWrite))
            {
                OpcPackageSignatureBuilder signerBuilder = package.CreateSignatureBuilder();
                signerBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();
                signerBuilder.Sign(
                    new SignConfigurationSet(
                        publicCertificate: certificate,
                        signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                        fileDigestAlgorithm: HashAlgorithmName.SHA256,
                        signingKey: rsaPrivateKey!));
            }
            using (var netfxPackage = OpcPackage.Open(path))
            {
                Assert.NotEmpty(netfxPackage.GetSignatures());
            }
        }

        [Fact]
        public void ShouldRemoveSignature()
        {
            string path;
            using (X509Certificate2 certificate = _pfxFilesFixture.GetPfx(keySizeInBits: 2048, HashAlgorithmName.SHA256))
            using (RSA? rsaPrivateKey = certificate.GetRSAPrivateKey())
            using (OpcPackage package = ShadowCopyPackage(SamplePackage, out path, OpcPackageFileMode.ReadWrite))
            {
                OpcPackageSignatureBuilder signerBuilder = package.CreateSignatureBuilder();
                signerBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();
                signerBuilder.Sign(
                    new SignConfigurationSet(
                        publicCertificate: certificate,
                        signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                        fileDigestAlgorithm: HashAlgorithmName.SHA256,
                        signingKey: rsaPrivateKey!));
            }
            using (OpcPackage package = OpcPackage.Open(path, OpcPackageFileMode.ReadWrite))
            {
                List<OpcSignature> signatures = package.GetSignatures().ToList();
                Assert.Single(signatures);
                OpcSignature signature = signatures[0];
                signature.Remove();
                Assert.Null(signature.Part);
                Assert.Throws<InvalidOperationException>(() => signature.CreateTimestampBuilder());
                Assert.Empty(package.GetSignatures());
            }
        }

        [Fact]
        public void ShouldUseInvariantCultureForContextCreationTime()
        {
            CultureInfo originalCulture = CultureInfo.CurrentCulture;

            try
            {
                // This test only works if the current culture is one of a set of cultures that includes en-DK that
                // that repro the original bug.  However, because tests should not rely on a specific culture being
                // installed, we'll create a custom culture just for this test.
                var customCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();

                customCulture.DateTimeFormat.TimeSeparator = ".";

                CultureInfo.CurrentCulture = customCulture;

                using (OpcPackage package = ShadowCopyPackage(
                    SamplePackage,
                    out string? path,
                    OpcPackageFileMode.ReadWrite))
                {
                    OpcPackageSignatureBuilder signerBuilder = package.CreateSignatureBuilder();

                    signerBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();

                    using (X509Certificate2 certificate = _pfxFilesFixture.GetPfx(
                        keySizeInBits: 3072,
                        HashAlgorithmName.SHA384))
                    using (RSA? rsaPrivateKey = certificate.GetRSAPrivateKey())
                    {
                        OpcSignature signature = signerBuilder.Sign(
                            new SignConfigurationSet(
                                publicCertificate: certificate,
                                signatureDigestAlgorithm: HashAlgorithmName.SHA384,
                                fileDigestAlgorithm: HashAlgorithmName.SHA384,
                                signingKey: rsaPrivateKey!));

                        using (Stream stream = signature.Part!.Open())
                        {
                            XmlDocument document = new();

                            document.Load(stream);

                            XmlNode? signatureTimeValueElement = document.GetElementsByTagName("Value")[0];

                            Assert.NotNull(signatureTimeValueElement);

                            const string expectedFormat = "yyyy-MM-ddTHH:mm:ss.fzzz";

                            bool isValidFormat = DateTimeOffset.TryParseExact(
                                signatureTimeValueElement.InnerText,
                                expectedFormat,
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out DateTimeOffset parsedDateTime);

                            Assert.True(isValidFormat, $"The date time string '{signatureTimeValueElement.InnerText}' does not match the expected format '{expectedFormat}'.");
                        }
                    }
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        public static IEnumerable<object[]> RsaTimestampTheories
        {
            get
            {
                yield return new object[] { 2048, HashAlgorithmName.SHA256, HashAlgorithmName.SHA256 };
            }
        }

        private OpcPackage ShadowCopyPackage(string packagePath, out string path, OpcPackageFileMode mode = OpcPackageFileMode.Read)
        {
            string temp = Path.GetTempFileName();
            _shadowFiles.Add(temp);
            File.Copy(packagePath, temp, overwrite: true);
            path = temp;
            return OpcPackage.Open(temp, mode);
        }

        public void Dispose()
        {
            void CleanUpShadows()
            {
                _shadowFiles.ForEach(File.Delete);
            }
            CleanUpShadows();
        }
    }
}
