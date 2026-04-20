// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using AzureSign.Core;
using Microsoft.Extensions.Logging;
using Moq;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    [Collection(SigningTestsCollection.Name)]
    public sealed class AzureSignToolSignerTests : IDisposable
    {
        private readonly TrustedCertificateFixture _certificateFixture;
        private readonly DirectoryService _directoryService;
        private readonly AzureSignToolSigner _signer;

        public AzureSignToolSignerTests(TrustedCertificateFixture certificateFixture)
        {
            ArgumentNullException.ThrowIfNull(certificateFixture, nameof(certificateFixture));

            _certificateFixture = certificateFixture;
            _directoryService = new(Mock.Of<ILogger<IDirectoryService>>());
            _signer = new AzureSignToolSigner(
                Mock.Of<IToolConfigurationProvider>(),
                Mock.Of<ISignatureAlgorithmProvider>(),
                Mock.Of<ICertificateProvider>(),
                Mock.Of<ILogger<IDataFormatSigner>>());
        }

        public void Dispose()
        {
            _directoryService.Dispose();
        }

        [Fact]
        public void CanSign_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _signer.CanSign(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Theory]
        [InlineData(".appx")]
        [InlineData(".appxbundle")]
        [InlineData(".cab")]
        [InlineData(".cat")]
        [InlineData(".cdxml")]
        [InlineData(".dll")]
        [InlineData(".eappx")]
        [InlineData(".eappxbundle")]
        [InlineData(".emsix")]
        [InlineData(".emsixbundle")]
        [InlineData(".exe")]
        [InlineData(".js")]
        [InlineData(".msi")]
        [InlineData(".msix")]
        [InlineData(".msixbundle")]
        [InlineData(".msm")]
        [InlineData(".msp")]
        [InlineData(".mst")]
        [InlineData(".ocx")]
        [InlineData(".ps1")]
        [InlineData(".ps1xml")]
        [InlineData(".psd1")]
        [InlineData(".psm1")]
        [InlineData(".stl")]
        [InlineData(".sys")]
        [InlineData(".vbs")]
        [InlineData(".vxd")]
        [InlineData(".winmd")]
        public void CanSign_WhenFileExtensionMatches_ReturnsTrue(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.True(_signer.CanSign(file));
        }


        [Fact]
        public void CanSign_WithNonDynamicsBusinessCentralAppFile_ReturnsFalse()
        {
            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                FileInfo file = new(Path.Combine(temporaryDirectory.Directory.FullName, "file.app"));

                File.WriteAllText(file.FullName, "{}");

                Assert.False(_signer.CanSign(file));
            }
        }

        [Fact]
        public void CanSign_WithDynamicsBusinessCentralAppFile_ReturnsTrue()
        {
            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                FileInfo file = new(Path.Combine(temporaryDirectory.Directory.FullName, "file.app"));

                File.WriteAllBytes(file.FullName, new byte[] { 0x4e, 0x41, 0x56, 0x58 });

                Assert.True(_signer.CanSign(file));
            }
        }

        [Theory]
        [InlineData(".txt")]
        [InlineData(".msİ")] // Turkish İ (U+0130)
        [InlineData(".msı")] // Turkish ı (U+0131)
        public void CanSign_WhenFileExtensionDoesNotMatch_ReturnsFalse(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.False(_signer.CanSign(file));
        }

        [RequiresElevationTheory]
        [InlineData("cmdlet-definition.cdxml")]
        [InlineData("script.ps1")]
        [InlineData("data.psd1")]
        [InlineData("module.psm1")]
        [InlineData("formatting.ps1xml")]
        public async Task SignAsync_WhenFileIsSupported_Signs(string fileName)
        {
            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                FileInfo file = TestAssets.GetTestAsset(temporaryDirectory.Directory, "PowerShell", fileName);

                SignOptions options = new(
                    applicationName: null,
                    publisherName: null,
                    description: null,
                    descriptionUrl: null,
                    HashAlgorithmName.SHA256,
                    HashAlgorithmName.SHA256,
                    timestampService: null!,
                    matcher: null,
                    antiMatcher: null,
                    recurseContainers: true);

                X509Certificate2 certificate = _certificateFixture.TrustedCertificate;

                using (RSA privateKey = certificate.GetRSAPrivateKey()!)
                {
                    ToolConfigurationProvider toolConfigurationProvider = new(new AppRootDirectoryLocator());
                    Mock<ISignatureAlgorithmProvider> signatureAlgorithmProvider = new();
                    Mock<ICertificateProvider> certificateProvider = new();

                    certificateProvider.Setup(x => x.GetCertificateAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new X509Certificate2(certificate));

                    signatureAlgorithmProvider.Setup(x => x.GetRsaAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(privateKey);

                    ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();

                    AzureSignToolSigner signer = new(
                        toolConfigurationProvider,
                        signatureAlgorithmProvider.Object,
                        certificateProvider.Object,
                        logger);

                    await signer.SignAsync(new[] { file }, options);

                    // Verify that the file has been renamed back.
                    file.Refresh();

                    Assert.True(file.Exists);

                    PowerShellFileReader reader = PowerShellFileReader.Read(file);

                    Assert.True(reader.TryGetSignature(out SignedCms? signedCms));

                    SignerInfo signerInfo = (SignerInfo)Assert.Single(signedCms.SignerInfos)!;

                    Assert.True(certificate.RawDataMemory.Span.SequenceEqual(signerInfo.Certificate!.RawDataMemory.Span));

                    signedCms.CheckSignature(verifySignatureOnly: true);

                    signatureAlgorithmProvider.VerifyAll();
                    certificateProvider.VerifyAll();
                }
            }
        }

        [Theory]
        [InlineData(".vbs")]
        [InlineData(".VBS")]
        [InlineData(".Vbs")]
        public async Task SignAsync_WithStaRequiredFile_SignsOnStaThread(string extension)
        {
            using RSA rsa = RSA.Create(keySizeInBits: 2048);
            CertificateRequest req = new("CN=Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 certificate = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddDays(1));

            Mock<ISignatureAlgorithmProvider> signatureAlgorithmProvider = new();
            Mock<ICertificateProvider> certificateProvider = new();

            certificateProvider.Setup(x => x.GetCertificateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new X509Certificate2(certificate.Export(X509ContentType.Pfx)));

            signatureAlgorithmProvider.Setup(x => x.GetRsaAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(RSA.Create(rsa.ExportParameters(includePrivateParameters: true)));

            SignOptions options = new(
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256,
                timestampService: null!,
                matcher: null,
                antiMatcher: null,
                recurseContainers: true);

            TestableAzureSignToolSigner signer = new(
                Mock.Of<IToolConfigurationProvider>(),
                signatureAlgorithmProvider.Object,
                certificateProvider.Object,
                Mock.Of<ILogger<IDataFormatSigner>>());

            FileInfo file = new($"test{extension}");

            await signer.SignAsync(new[] { file }, options);

            Assert.Single(signer.SigningCalls);
            (string FileName, ApartmentState ApartmentState) call = signer.SigningCalls.Single();
            Assert.Equal(ApartmentState.STA, call.ApartmentState);
        }

        [Fact]
        public async Task SignAsync_WithMixedFiles_SignsStaFilesOnStaAndNonStaInParallel()
        {
            using RSA rsa = RSA.Create(keySizeInBits: 2048);
            CertificateRequest req = new("CN=Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 certificate = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddDays(1));

            Mock<ISignatureAlgorithmProvider> signatureAlgorithmProvider = new();
            Mock<ICertificateProvider> certificateProvider = new();

            certificateProvider.Setup(x => x.GetCertificateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new X509Certificate2(certificate.Export(X509ContentType.Pfx)));

            signatureAlgorithmProvider.Setup(x => x.GetRsaAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(RSA.Create(rsa.ExportParameters(includePrivateParameters: true)));

            SignOptions options = new(
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256,
                timestampService: null!,
                matcher: null,
                antiMatcher: null,
                recurseContainers: true);

            TestableAzureSignToolSigner signer = new(
                Mock.Of<IToolConfigurationProvider>(),
                signatureAlgorithmProvider.Object,
                certificateProvider.Object,
                Mock.Of<ILogger<IDataFormatSigner>>());

            FileInfo[] files = new[]
            {
                new FileInfo("script1.vbs"),
                new FileInfo("library.dll"),
                new FileInfo("script2.js"),
                new FileInfo("app.exe"),
                new FileInfo("script3.VBS"),
            };

            await signer.SignAsync(files, options);

            Assert.Equal(5, signer.SigningCalls.Count);

            (string FileName, ApartmentState ApartmentState)[] calls = signer.SigningCalls.ToArray();

            // STA files should be signed first (sequential, before the parallel batch)
            // and all on STA threads
            HashSet<string> staCallNames = new(StringComparer.OrdinalIgnoreCase) { "script1.vbs", "script2.js", "script3.VBS" };
            (string FileName, ApartmentState ApartmentState)[] staCalls = calls.Where(c => staCallNames.Contains(c.FileName)).ToArray();
            Assert.Equal(3, staCalls.Length);
            Assert.All(staCalls, c => Assert.Equal(ApartmentState.STA, c.ApartmentState));

            // Non-STA files should be signed on MTA threads
            (string FileName, ApartmentState ApartmentState)[] mtaCalls = calls.Where(c => !staCallNames.Contains(c.FileName)).ToArray();
            Assert.Equal(2, mtaCalls.Length);
            Assert.All(mtaCalls, c => Assert.Equal(ApartmentState.MTA, c.ApartmentState));

            // STA files should appear before non-STA files in the signing order
            // (since STA files are signed sequentially first)
            int lastStaIndex = Array.FindLastIndex(calls, c => staCallNames.Contains(c.FileName));
            int firstMtaIndex = Array.FindIndex(calls, c => !staCallNames.Contains(c.FileName));
            Assert.True(lastStaIndex < firstMtaIndex,
                $"STA files should be signed before non-STA files. Last STA index: {lastStaIndex}, First MTA index: {firstMtaIndex}");
        }

        [Fact]
        public async Task SignAsync_WithNonStaFile_SignsOnMtaThread()
        {
            using RSA rsa = RSA.Create(keySizeInBits: 2048);
            CertificateRequest req = new("CN=Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 certificate = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddDays(1));

            Mock<ISignatureAlgorithmProvider> signatureAlgorithmProvider = new();
            Mock<ICertificateProvider> certificateProvider = new();

            certificateProvider.Setup(x => x.GetCertificateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new X509Certificate2(certificate.Export(X509ContentType.Pfx)));

            signatureAlgorithmProvider.Setup(x => x.GetRsaAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(RSA.Create(rsa.ExportParameters(includePrivateParameters: true)));

            SignOptions options = new(
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256,
                timestampService: null!,
                matcher: null,
                antiMatcher: null,
                recurseContainers: true);

            TestableAzureSignToolSigner signer = new(
                Mock.Of<IToolConfigurationProvider>(),
                signatureAlgorithmProvider.Object,
                certificateProvider.Object,
                Mock.Of<ILogger<IDataFormatSigner>>());

            FileInfo file = new("test.dll");

            await signer.SignAsync(new[] { file }, options);

            Assert.Single(signer.SigningCalls);
            (string FileName, ApartmentState ApartmentState) call = signer.SigningCalls.Single();
            Assert.Equal(ApartmentState.MTA, call.ApartmentState);
        }
    }

    internal class TestableAzureSignToolSigner : AzureSignToolSigner
    {
        public ConcurrentQueue<(string FileName, ApartmentState ApartmentState)> SigningCalls { get; } = new();

        public TestableAzureSignToolSigner(
            IToolConfigurationProvider toolConfigurationProvider,
            ISignatureAlgorithmProvider signatureAlgorithmProvider,
            ICertificateProvider certificateProvider,
            ILogger<IDataFormatSigner> logger)
            : base(toolConfigurationProvider, signatureAlgorithmProvider, certificateProvider, logger)
        {
        }

        internal override int SignFileCore(
            AuthenticodeKeyVaultSigner signer,
            FileInfo file,
            SignOptions options,
            FileInfo manifestFile)
        {
            SigningCalls.Enqueue((file.Name, Thread.CurrentThread.GetApartmentState()));
            return S_OK;
        }
    }
}
