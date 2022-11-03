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

        [Fact]
        public void CanSign_WhenFileExtensionDoesNotMatch_ReturnsFalse()
        {
            FileInfo file = new("file.txt");

            Assert.False(_provider.CanSign(file));
        }
    }
}