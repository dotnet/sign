// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    public class NuGetSignatureProviderTests
    {
        private readonly NuGetSignatureProvider _provider;

        public NuGetSignatureProviderTests()
        {
            _provider = new NuGetSignatureProvider(
                Mock.Of<IKeyVaultService>(),
                Mock.Of<INuGetSignTool>(),
                Mock.Of<ILogger<ISignatureProvider>>());
        }

        [Fact]
        public void Constructor_WhenKeyVaultServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new NuGetSignatureProvider(
                    keyVaultService: null!,
                    Mock.Of<INuGetSignTool>(),
                    Mock.Of<ILogger<ISignatureProvider>>()));

            Assert.Equal("keyVaultService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenNuGetSignToolIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new NuGetSignatureProvider(
                    Mock.Of<IKeyVaultService>(),
                    nuGetSignTool: null!,
                    Mock.Of<ILogger<ISignatureProvider>>()));

            Assert.Equal("nuGetSignTool", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new NuGetSignatureProvider(
                    Mock.Of<IKeyVaultService>(),
                    Mock.Of<INuGetSignTool>(),
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
        [InlineData(".nupkg")]
        [InlineData(".NUPKG")] // test case insensitivity
        [InlineData(".snupkg")]
        public void CanSign_WhenFileExtensionMatches_ReturnsTrue(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.True(_provider.CanSign(file));
        }

        [Fact]
        public void CanSign_WhenFileExtensionDoesNotMatch_ReturnsFalse()
        {
            FileInfo file = new("file.txt");

            Assert.False(_provider.CanSign(file));
        }
    }
}