// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Sign.Core.Timestamp;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core.Test
{
    public class OpcPackageSigningTests : IDisposable
    {
        private static readonly string SamplePackage = Path.Combine(".\\TestAssets\\VSIXSamples", "OpenVsixSignToolTest.vsix");
        private static readonly string SamplePackageSigned = Path.Combine(".\\TestAssets\\VSIXSamples", "OpenVsixSignToolTest-Signed.vsix");
        private readonly List<string> _shadowFiles = new List<string>();

        private static string CertPath(string str) => Path.Combine(".\\TestAssets\\certs", str);


        [Theory]
        [MemberData(nameof(RsaSigningTheories))]
        public void ShouldSignFileWithRsa(string pfxPath, HashAlgorithmName fileDigestAlgorithm, string expectedAlgorithm)
        {
            _ = expectedAlgorithm;

            using (var package = ShadowCopyPackage(SamplePackage, out string path, OpcPackageFileMode.ReadWrite))
            {
                var builder = package.CreateSignatureBuilder();
                builder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();
                using var certificate = new X509Certificate2(pfxPath, "test");
                using var rsaPrivateKey = certificate.GetRSAPrivateKey();
                var result = builder.Sign(
                 new SignConfigurationSet(
                    publicCertificate: certificate,
                    signatureDigestAlgorithm: fileDigestAlgorithm,
                    fileDigestAlgorithm: fileDigestAlgorithm,
                    signingKey: rsaPrivateKey!
                ));
                Assert.NotNull(result);
            }
        }

        public static IEnumerable<object[]> RsaSigningTheories
        {
            get
            {
                yield return new object[] { CertPath("rsa-2048-Sha256.pfx"), HashAlgorithmName.SHA512, OpcKnownUris.SignatureAlgorithms.RsaSHA512.AbsoluteUri };
                yield return new object[] { CertPath("rsa-2048-Sha256.pfx"), HashAlgorithmName.SHA384, OpcKnownUris.SignatureAlgorithms.RsaSHA384.AbsoluteUri };
                yield return new object[] { CertPath("rsa-2048-Sha256.pfx"), HashAlgorithmName.SHA256, OpcKnownUris.SignatureAlgorithms.RsaSHA256.AbsoluteUri };
            }
        }

        [Theory]
        [MemberData(nameof(RsaTimestampTheories))]
        public async Task ShouldTimestampFileWithRsa(string pfxPath, HashAlgorithmName timestampDigestAlgorithm)
        {
            using (var package = ShadowCopyPackage(SamplePackage, out var path, OpcPackageFileMode.ReadWrite))
            {
                var signerBuilder = package.CreateSignatureBuilder();
                signerBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();
                using var certificate = new X509Certificate2(pfxPath, "test");
                using var rsaPrivateKey = certificate.GetRSAPrivateKey();
                var signature = signerBuilder.Sign(
                    new SignConfigurationSet(
                        publicCertificate: certificate,
                        signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                        fileDigestAlgorithm: HashAlgorithmName.SHA256,
                        signingKey: rsaPrivateKey!
                ));
                var timestampBuilder = signature.CreateTimestampBuilder();
                var result = await timestampBuilder.SignAsync(new Uri("http://timestamp.digicert.com"), timestampDigestAlgorithm);
                Assert.Equal(TimestampResult.Success, result);
            }
        }

        [Fact]
        public void ShouldSupportReSigning()
        {
            string path;
            using var certificate = new X509Certificate2(CertPath("rsa-2048-Sha256.pfx"), "test");
            using var rsaPrivateKey = certificate.GetRSAPrivateKey();
            using (var package = ShadowCopyPackage(SamplePackage, out path, OpcPackageFileMode.ReadWrite))
            {
                var signerBuilder = package.CreateSignatureBuilder();
                signerBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();
                signerBuilder.Sign(
                 new SignConfigurationSet(
                    publicCertificate: certificate,
                    signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                    fileDigestAlgorithm: HashAlgorithmName.SHA256,
                    signingKey: rsaPrivateKey!
                ));
            }
            using (var package = OpcPackage.Open(path, OpcPackageFileMode.ReadWrite))
            {
                var signerBuilder = package.CreateSignatureBuilder();
                signerBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();
                signerBuilder.Sign(
                 new SignConfigurationSet(
                    publicCertificate: certificate,
                    signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                    fileDigestAlgorithm: HashAlgorithmName.SHA256,
                    signingKey: rsaPrivateKey!
                ));
            }
            using (var netfxPackage = OpcPackage.Open(path))
            {
                Assert.NotEmpty(netfxPackage.GetSignatures());
            }
        }

        [Fact]
        public void ShouldSupportReSigningWithDifferentCertificate()
        {
            string path;
            using (var package = ShadowCopyPackage(SamplePackage, out path, OpcPackageFileMode.ReadWrite))
            {
                var signerBuilder = package.CreateSignatureBuilder();
                signerBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();
                using var rsaSha1Cert = new X509Certificate2(CertPath("rsa-2048-sha1.pfx"), "test");
                using var rsaPrivateKey = rsaSha1Cert.GetRSAPrivateKey();
                signerBuilder.Sign(
                 new SignConfigurationSet(
                    publicCertificate: rsaSha1Cert,
                    signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                    fileDigestAlgorithm: HashAlgorithmName.SHA256,
                    signingKey: rsaPrivateKey!
                ));
            }
            using (var package = OpcPackage.Open(path, OpcPackageFileMode.ReadWrite))
            {
                var signerBuilder = package.CreateSignatureBuilder();
                signerBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();
                using var rsaSha256Cert = new X509Certificate2(CertPath("rsa-2048-Sha256.pfx"), "test");
                using var rsaPrivateKey = rsaSha256Cert.GetRSAPrivateKey();
                signerBuilder.Sign(
                 new SignConfigurationSet(
                    publicCertificate: rsaSha256Cert,
                    signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                    fileDigestAlgorithm: HashAlgorithmName.SHA256,
                    signingKey: rsaPrivateKey!
                ));
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
            using (var package = ShadowCopyPackage(SamplePackage, out path, OpcPackageFileMode.ReadWrite))
            {
                using var certificate = new X509Certificate2(CertPath("rsa-2048-Sha256.pfx"), "test");
                using var rsaPrivateKey = certificate.GetRSAPrivateKey();
                var signerBuilder = package.CreateSignatureBuilder();
                signerBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();
                signerBuilder.Sign(
                 new SignConfigurationSet(
                    publicCertificate: certificate,
                    signatureDigestAlgorithm: HashAlgorithmName.SHA256,
                    fileDigestAlgorithm: HashAlgorithmName.SHA256,
                    signingKey: rsaPrivateKey!
                ));
            }
            using (var package = OpcPackage.Open(path, OpcPackageFileMode.ReadWrite))
            {
                var signatures = package.GetSignatures().ToList();
                Assert.Single(signatures);
                var signature = signatures[0];
                signature.Remove();
                Assert.Null(signature.Part);
                Assert.Throws<InvalidOperationException>(() => signature.CreateTimestampBuilder());
                Assert.Empty(package.GetSignatures());
            }
        }

        public static IEnumerable<object[]> RsaTimestampTheories
        {
            get
            {
                yield return new object[] { CertPath("rsa-2048-Sha256.pfx"), HashAlgorithmName.SHA256 };
            }
        }

        private OpcPackage ShadowCopyPackage(string packagePath, out string path, OpcPackageFileMode mode = OpcPackageFileMode.Read)
        {
            var temp = Path.GetTempFileName();
            _shadowFiles.Add(temp);
            File.Copy(packagePath, temp, true);
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
