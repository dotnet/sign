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
    public class NuGetSignerTests
    {
        private readonly NuGetSigner _signer;

        public NuGetSignerTests()
        {
            _signer = new NuGetSigner(
                Mock.Of<ISignatureAlgorithmProvider>(),
                Mock.Of<ICertificateProvider>(),
                Mock.Of<INuGetSignTool>(),
                Mock.Of<ILogger<IDataFormatSigner>>());
        }

        [Fact]
        public void Constructor_WhenSignatureAlgorithmProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new NuGetSigner(
                    signatureAlgorithmProvider: null!,
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<INuGetSignTool>(),
                    Mock.Of<ILogger<IDataFormatSigner>>()));

            Assert.Equal("signatureAlgorithmProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new NuGetSigner(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    certificateProvider: null!,
                    Mock.Of<INuGetSignTool>(),
                    Mock.Of<ILogger<IDataFormatSigner>>()));

            Assert.Equal("certificateProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenNuGetSignToolIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new NuGetSigner(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    nuGetSignTool: null!,
                    Mock.Of<ILogger<IDataFormatSigner>>()));

            Assert.Equal("nuGetSignTool", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new NuGetSigner(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<INuGetSignTool>(),
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
        [InlineData(".nupkg")]
        [InlineData(".NUPKG")] // test case insensitivity
        [InlineData(".snupkg")]
        public void CanSign_WhenFileExtensionMatches_ReturnsTrue(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.True(_signer.CanSign(file));
        }

        [Fact]
        public void CanSign_WhenFileExtensionDoesNotMatch_ReturnsFalse()
        {
            FileInfo file = new("file.txt");

            Assert.False(_signer.CanSign(file));
        }

        [Fact]
        public async Task SignAsync_WhenSigningFails_Throws()
        {
            Mock<INuGetSignTool> nuGetSignTool = new();

            nuGetSignTool.Setup(
                x => x.SignAsync(
                    It.IsNotNull<FileInfo>(),
                    It.IsNotNull<RSA>(),
                    It.IsNotNull<X509Certificate2>(),
                    It.IsNotNull<SignOptions>()))
                .Returns(Task.FromResult(false));

            NuGetSigner signer = new(
                Mock.Of<ISignatureAlgorithmProvider>(),
                Mock.Of<ICertificateProvider>(),
                nuGetSignTool.Object,
                Mock.Of<ILogger<IDataFormatSigner>>());

            signer.Retry = TimeSpan.FromMicroseconds(1);

            SignOptions options = new(
                "ApplicationName",
                "PublisherName",
                "Description",
                new Uri("https://description.test"),
                HashAlgorithmName.SHA384,
                HashAlgorithmName.SHA384,
                new Uri("http://timestamp.test"),
                matcher: null,
                antiMatcher: null,
                recurseContainers: true);

            using (DirectoryService directoryService = new(Mock.Of<ILogger<IDirectoryService>>()))
            using (TemporaryDirectory temporaryDirectory = new(directoryService))
            {
                FileInfo nupkgFile = TestFileCreator.CreateEmptyZipFile(temporaryDirectory, fileExtension: ".nupkg");

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
                using (RSA privateKey = certificate.GetRSAPrivateKey()!)
                {
                    await Assert.ThrowsAsync<SigningException>(() => signer.SignAsync([nupkgFile], options));
                }
            }
        }
    }
}
