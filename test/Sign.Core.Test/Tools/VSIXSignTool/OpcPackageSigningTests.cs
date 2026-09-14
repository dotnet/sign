// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using System.IO.Compression;
using System.IO.Packaging;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.Core.Timestamp;
using Sign.TestInfrastructure;
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
        [InlineData("ab#c.txt")]
        [InlineData("folder/ab#c.txt")]
        [InlineData("ab?c.txt")]
        [InlineData("folder/ab?c.txt")]
        public async Task SignAsync_WithRawUriDelimiterInZipEntryName_Throws(string entryName)
        {
            string path = CreatePackageWithEntry(entryName);
            IReadOnlyDictionary<string, byte[]> originalEntries = ReadArchiveEntries(path);
            VsixSignTool signTool = new(Substitute.For<ILogger<IVsixSignTool>>());

            using (X509Certificate2 certificate = _pfxFilesFixture.GetPfx(
                keySizeInBits: 2048,
                HashAlgorithmName.SHA256))
            using (RSA? rsaPrivateKey = certificate.GetRSAPrivateKey())
            {
                SignConfigurationSet configuration = new(
                    publicCertificate: certificate,
                    signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                    fileDigestAlgorithm: HashAlgorithmName.SHA256,
                    signingKey: rsaPrivateKey!);
                SignOptions options = new(
                    fileHashAlgorithm: HashAlgorithmName.SHA256,
                    timestampService: null!);

                InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
                    () => signTool.SignAsync(new FileInfo(path), configuration, options));

                Assert.Contains(entryName, exception.Message, StringComparison.Ordinal);
                Assert.Contains(entryName.Contains('#') ? "#" : "?", exception.Message, StringComparison.Ordinal);
            }

            using (OpcPackage package = OpcPackage.Open(path))
            {
                Assert.Empty(package.GetSignatures());
            }

            AssertArchiveEntriesEqual(originalEntries, ReadArchiveEntries(path));
        }

        [Theory]
        [InlineData("abc.txt")]
        [InlineData("folder/abc.txt")]
        public async Task SignAsync_WithValidZipEntryName_Succeeds(string entryName)
        {
            string path = CreatePackageWithEntry(entryName);
            VsixSignTool signTool = new(Substitute.For<ILogger<IVsixSignTool>>());

            using (X509Certificate2 certificate = _pfxFilesFixture.GetPfx(
                keySizeInBits: 2048,
                HashAlgorithmName.SHA256))
            using (RSA? rsaPrivateKey = certificate.GetRSAPrivateKey())
            {
                SignConfigurationSet configuration = new(
                    publicCertificate: certificate,
                    signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                    fileDigestAlgorithm: HashAlgorithmName.SHA256,
                    signingKey: rsaPrivateKey!);
                SignOptions options = new(
                    fileHashAlgorithm: HashAlgorithmName.SHA256,
                    timestampService: null!);

                Assert.True(await signTool.SignAsync(new FileInfo(path), configuration, options));
            }

            using (OpcPackage package = OpcPackage.Open(path))
            {
                Assert.Single(package.GetSignatures());
            }
        }

        [Theory]
        [InlineData("ab%23c.txt")]
        [InlineData("folder/ab%23c.txt")]
        [InlineData("ab%3Fc.txt")]
        [InlineData("folder/ab%3fc.txt")]
        public void PartNameValidation_WithPercentEncodedUriDelimiter_DoesNotThrow(string entryName)
        {
            OpcPartNameValidator.ThrowIfContainsUnsupportedUriDelimiter(entryName);
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

        [Theory]
        [InlineData("ab%23c.txt", "/ab%23c.txt?ContentType=text/plain", "/ab#c.txt?ContentType=text/plain", "/ab%2523c.txt?ContentType=text/plain")]
        [InlineData("nested/ab%3Fc.txt", "/nested/ab%3Fc.txt?ContentType=text/plain", "/nested/ab?c.txt?ContentType=text/plain", "/nested/ab%253Fc.txt?ContentType=text/plain")]
        [InlineData("ab%25c.txt", "/ab%25c.txt?ContentType=text/plain", null, "/ab%2525c.txt?ContentType=text/plain")]
        [InlineData("nested/ordinary.txt", "/nested/ordinary.txt?ContentType=text/plain", null, null)]
        [InlineData("caf%C3%A9.txt", "/caf%C3%A9.txt?ContentType=text/plain", "/café.txt?ContentType=text/plain", "/caf%25C3%25A9.txt?ContentType=text/plain")]
        public void ShouldPreservePartNameInVerifiableSignature(
            string partName,
            string expectedReference,
            string? incorrectlyDecodedReference,
            string? doubleEncodedReference)
        {
            string path = CreatePackage(partName);

            using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
            using (RSA? rsaPrivateKey = certificate.GetRSAPrivateKey())
            using (OpcPackage package = OpcPackage.Open(path, OpcPackageFileMode.ReadWrite))
            {
                OpcPart part = Assert.IsType<OpcPart>(
                    package.GetPart(new Uri($"/{partName}", UriKind.Relative)));
                Assert.Equal(partName, part.Entry.FullName);

                OpcPackageSignatureBuilder signerBuilder = package.CreateSignatureBuilder();
                signerBuilder.EnqueuePart(part);
                signerBuilder.Sign(
                    new SignConfigurationSet(
                        publicCertificate: certificate,
                        signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                        fileDigestAlgorithm: HashAlgorithmName.SHA256,
                        signingKey: rsaPrivateKey!));
            }

            IReadOnlyList<string> references = ReadManifestReferences(path);

            Assert.Contains(expectedReference, references);

            if (incorrectlyDecodedReference != null)
            {
                Assert.DoesNotContain(incorrectlyDecodedReference, references);
            }

            if (doubleEncodedReference != null)
            {
                Assert.DoesNotContain(doubleEncodedReference, references);
            }

            using (Package package = Package.Open(path, FileMode.Open, FileAccess.Read))
            {
                var signatureManager = new PackageDigitalSignatureManager(package);

                Assert.True(signatureManager.IsSigned);
                Assert.Equal(VerifyResult.Success, signatureManager.VerifySignatures(exitOnFailure: false));
            }
        }

        public static IEnumerable<object[]> RsaTimestampTheories
        {
            get
            {
                yield return new object[] { 2048, HashAlgorithmName.SHA256, HashAlgorithmName.SHA256 };
            }
        }

        private string CreatePackage(string partName)
        {
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():D}.vsix");
            _shadowFiles.Add(path);

            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                WriteEntry(
                    archive,
                    "[Content_Types].xml",
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                      <Default Extension="txt" ContentType="text/plain" />
                    </Types>
                    """);
                WriteEntry(archive, partName, "content");
            }

            return path;
        }

        private string CreatePackageWithEntry(string entryName)
        {
            string path = Path.GetTempFileName();
            _shadowFiles.Add(path);

            using (FileStream stream = new(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
            {
                WriteEntry(
                    archive,
                    "[Content_Types].xml",
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                      <Default Extension="txt" ContentType="text/plain" />
                    </Types>
                    """);
                WriteEntry(archive, entryName, "test");
            }

            return path;
        }

        private static IReadOnlyList<string> ReadManifestReferences(string path)
        {
            using ZipArchive archive = ZipFile.OpenRead(path);
            ZipArchiveEntry signatureEntry = Assert.Single(
                archive.Entries,
                entry => entry.FullName.EndsWith(".psdsxs", StringComparison.Ordinal));
            using Stream stream = signatureEntry.Open();
            XDocument document = XDocument.Load(stream);
            XNamespace digitalSignature = "http://www.w3.org/2000/09/xmldsig#";

            return document
                .Descendants(digitalSignature + "Manifest")
                .Elements(digitalSignature + "Reference")
                .Select(reference => Assert.IsType<XAttribute>(reference.Attribute("URI")).Value)
                .ToList();
        }

        private static void WriteEntry(ZipArchive archive, string entryName, string content)
        {
            using StreamWriter writer = new(archive.CreateEntry(entryName).Open());
            writer.Write(content);
        }

        private static IReadOnlyDictionary<string, byte[]> ReadArchiveEntries(string path)
        {
            using ZipArchive archive = ZipFile.OpenRead(path);
            Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                using Stream stream = entry.Open();
                using MemoryStream content = new();
                stream.CopyTo(content);
                entries.Add(entry.FullName, content.ToArray());
            }

            return entries;
        }

        private static void AssertArchiveEntriesEqual(
            IReadOnlyDictionary<string, byte[]> expected,
            IReadOnlyDictionary<string, byte[]> actual)
        {
            Assert.Equal(expected.Keys.Order(), actual.Keys.Order());

            foreach ((string entryName, byte[] content) in expected)
            {
                Assert.Equal(content, actual[entryName]);
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
