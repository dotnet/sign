// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
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
                    antiMatcher: null);

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
    }
}
