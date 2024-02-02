// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    public class VsixSignatureProviderTests
    {
        private readonly VsixSignatureProvider _provider;

        public VsixSignatureProviderTests()
        {
            _provider = new VsixSignatureProvider(
                Mock.Of<ISignatureAlgorithmProvider>(),
                Mock.Of<ICertificateProvider>(),
                Mock.Of<IOpenVsixSignTool>(),
                Mock.Of<ILogger<ISignatureProvider>>());
        }

        [Fact]
        public void Constructor_WhenSignatureAlgorithmProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new VsixSignatureProvider(
                    signatureAlgorithmProvider: null!,
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IOpenVsixSignTool>(),
                    Mock.Of<ILogger<ISignatureProvider>>()));

            Assert.Equal("signatureAlgorithmProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new VsixSignatureProvider(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    certificateProvider: null!,
                    Mock.Of<IOpenVsixSignTool>(),
                    Mock.Of<ILogger<ISignatureProvider>>()));

            Assert.Equal("certificateProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenNuGetSignToolIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new VsixSignatureProvider(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    openVsixSignTool: null!,
                    Mock.Of<ILogger<ISignatureProvider>>()));

            Assert.Equal("openVsixSignTool", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new VsixSignatureProvider(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IOpenVsixSignTool>(),
                    logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public void CanSign_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _provider.CanSign(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Theory]
        [InlineData(".vsix")]
        [InlineData(".VSIX")] // test case insensitivity
        public void CanSign_WhenFileExtensionMatches_ReturnsTrue(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.True(_provider.CanSign(file));
        }

        [Theory]
        [InlineData(".txt")]
        [InlineData(".vsİx")] // Turkish İ (U+0130)
        [InlineData(".vsıx")] // Turkish ı (U+0131)
        public void CanSign_WhenFileExtensionDoesNotMatch_ReturnsFalse(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.False(_provider.CanSign(file));
        }
    }
}