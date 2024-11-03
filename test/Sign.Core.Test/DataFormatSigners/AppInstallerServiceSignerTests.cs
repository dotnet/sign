// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    public class AppInstallerServiceSignerTests
    {
        private readonly AppInstallerServiceSigner _signer;

        public AppInstallerServiceSignerTests()
        {
            _signer = new AppInstallerServiceSigner(
                Mock.Of<ICertificateProvider>(),
                Mock.Of<ILogger<IDataFormatSigner>>());
        }

        [Fact]
        public void Constructor_WhenCertificateProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppInstallerServiceSigner(
                    certificateProvider: null!,
                    Mock.Of<ILogger<IDataFormatSigner>>()));

            Assert.Equal("certificateProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppInstallerServiceSigner(
                    Mock.Of<ICertificateProvider>(),
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
        [InlineData(".appInstaller")] // Turkish I (U+0049)
        [InlineData(".appinstaller")] // Turkish i (U+0069)
        public void CanSign_WhenFileExtensionMatches_ReturnsTrue(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.True(_signer.CanSign(file));
        }

        [Theory]
        [InlineData(".txt")]
        [InlineData(".appİnstaller")] // Turkish İ (U+0130)
        [InlineData(".appınstaller")] // Turkish ı (U+0131)
        public void CanSign_WhenFileExtensionDoesNotMatch_ReturnsFalse(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.False(_signer.CanSign(file));
        }
    }
}