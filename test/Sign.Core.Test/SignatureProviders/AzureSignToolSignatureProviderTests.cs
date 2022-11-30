// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    public class AzureSignToolSignatureProviderTests
    {
        private readonly AzureSignToolSignatureProvider _provider;

        public AzureSignToolSignatureProviderTests()
        {
            _provider = new AzureSignToolSignatureProvider(
                Mock.Of<IToolConfigurationProvider>(),
                Mock.Of<IKeyVaultService>(),
                Mock.Of<ILogger<ISignatureProvider>>());
        }

        [Fact]
        public void CanSign_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _provider.CanSign(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Theory]
        [InlineData(".appx")]
        [InlineData(".appxbundle")]
        [InlineData(".cab")]
        [InlineData(".cat")]
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
        [InlineData(".psm1")]
        [InlineData(".stl")]
        [InlineData(".sys")]
        [InlineData(".vbs")]
        [InlineData(".vxd")]
        [InlineData(".winmd")]
        public void CanSign_WhenFileExtensionMatches_ReturnsTrue(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.True(_provider.CanSign(file));
        }

        [Theory]
        [InlineData(".txt")]
        [InlineData(".msİ")] // Turkish İ (U+0130)
        [InlineData(".msı")] // Turkish ı (U+0131)
        public void CanSign_WhenFileExtensionDoesNotMatch_ReturnsFalse(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.False(_provider.CanSign(file));
        }
    }
}