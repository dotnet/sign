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

        [Fact]
        public void CanSign_WhenFileExtensionDoesNotMatch_ReturnsFalse()
        {
            FileInfo file = new("file.txt");

            Assert.False(_provider.CanSign(file));
        }
    }
}