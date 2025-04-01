// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Moq;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public class VsixSignerTests
    {
        private readonly VsixSigner _signer;

        public VsixSignerTests()
        {
            _signer = new VsixSigner(
                Mock.Of<ISignatureAlgorithmProvider>(),
                Mock.Of<ICertificateProvider>(),
                Mock.Of<IVsixSignTool>(),
                Mock.Of<ILogger<IDataFormatSigner>>());
        }

        [Fact]
        public void Constructor_WhenSignatureAlgorithmProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new VsixSigner(
                    signatureAlgorithmProvider: null!,
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IVsixSignTool>(),
                    Mock.Of<ILogger<IDataFormatSigner>>()));

            Assert.Equal("signatureAlgorithmProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new VsixSigner(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    certificateProvider: null!,
                    Mock.Of<IVsixSignTool>(),
                    Mock.Of<ILogger<IDataFormatSigner>>()));

            Assert.Equal("certificateProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenNuGetSignToolIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new VsixSigner(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    vsixSignTool: null!,
                    Mock.Of<ILogger<IDataFormatSigner>>()));

            Assert.Equal("vsixSignTool", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new VsixSigner(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IVsixSignTool>(),
                    logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public void CanSign_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _signer.CanSign(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Theory]
        [InlineData(".vsix")]
        [InlineData(".VSIX")] // test case insensitivity
        public void CanSign_WhenFileExtensionMatches_ReturnsTrue(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.True(_signer.CanSign(file));
        }

        [Theory]
        [InlineData(".txt")]
        [InlineData(".vsİx")] // Turkish İ (U+0130)
        [InlineData(".vsıx")] // Turkish ı (U+0131)
        public void CanSign_WhenFileExtensionDoesNotMatch_ReturnsFalse(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.False(_signer.CanSign(file));
        }

        [Fact]
        public async Task SignAsync_WhenSigningFails_Throws()
        {
            SignOptions options = new(
                "ApplicationName",
                "PublisherName",
                "Description",
                new Uri("https://description.test"),
                HashAlgorithmName.SHA384,
                HashAlgorithmName.SHA384,
                new Uri("http://timestamp.test"),
                matcher: null,
                antiMatcher: null);

            using (DirectoryService directoryService = new(Mock.Of<ILogger<IDirectoryService>>()))
            using (TemporaryDirectory temporaryDirectory = new(directoryService))
            {
                FileInfo vsixFile = TestFileCreator.CreateEmptyZipFile(temporaryDirectory, fileExtension: ".vsix");

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
                using (RSA privateKey = certificate.GetRSAPrivateKey()!)
                {
                    Mock<IVsixSignTool> vsixSignTool = new();

                    SignConfigurationSet configuration = new(
                        options.FileHashAlgorithm,
                        options.FileHashAlgorithm,
                        privateKey,
                        certificate);

                    vsixSignTool.Setup(
                        x => x.SignAsync(
                            It.IsNotNull<FileInfo>(),
                            It.IsNotNull<SignConfigurationSet>(),
                            It.IsNotNull<SignOptions>()))
                        .Returns(Task.FromResult(false));

                    VsixSigner signer = new(
                        Mock.Of<ISignatureAlgorithmProvider>(),
                        Mock.Of<ICertificateProvider>(),
                        vsixSignTool.Object,
                        Mock.Of<ILogger<IDataFormatSigner>>());

                    signer.Retry = TimeSpan.FromMicroseconds(1);

                    await Assert.ThrowsAsync<SigningException>(() => signer.SignAsync([vsixFile], options));
                }
            }
        }
    }
}
