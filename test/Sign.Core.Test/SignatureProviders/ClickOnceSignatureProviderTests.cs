using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    public class ClickOnceSignatureProviderTests
    {
        private readonly ClickOnceSignatureProvider _provider;

        public ClickOnceSignatureProviderTests()
        {
            _provider = new ClickOnceSignatureProvider(
                Mock.Of<IKeyVaultService>(),
                Mock.Of<IContainerProvider>(),
                Mock.Of<IServiceProvider>(),
                Mock.Of<IDirectoryService>(),
                Mock.Of<IMageCli>(),
                Mock.Of<IManifestSigner>(),
                Mock.Of<ILogger<ISignatureProvider>>());
        }

        [Fact]
        public void CanSign_WhenFileExtensionMatches_ReturnsTrue()
        {
            FileInfo file = new("file.clickonce");

            Assert.True(_provider.CanSign(file));
        }

        [Theory]
        [InlineData(".txt")]
        [InlineData(".clİckonce")] // Turkish İ (U+0130)
        [InlineData(".clıckonce")] // Turkish ı (U+0131)
        public void CanSign_WhenFileExtensionDoesNotMatch_ReturnsFalse(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.False(_provider.CanSign(file));
        }
    }
}