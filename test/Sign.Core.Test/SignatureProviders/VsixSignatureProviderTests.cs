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
                Mock.Of<IKeyVaultService>(),
                Mock.Of<IOpenVsixSignTool>(),
                Mock.Of<ILogger<ISignatureProvider>>());
        }

        [Fact]
        public void Constructor_WhenKeyVaultServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new VsixSignatureProvider(
                    keyVaultService: null!,
                    Mock.Of<IOpenVsixSignTool>(),
                    Mock.Of<ILogger<ISignatureProvider>>()));

            Assert.Equal("keyVaultService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenNuGetSignToolIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new VsixSignatureProvider(
                    Mock.Of<IKeyVaultService>(),
                    openVsixSignTool: null!,
                    Mock.Of<ILogger<ISignatureProvider>>()));

            Assert.Equal("openVsixSignTool", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new VsixSignatureProvider(
                    Mock.Of<IKeyVaultService>(),
                    Mock.Of<IOpenVsixSignTool>(),
                    logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Theory]
        [InlineData(".vsix")]
        [InlineData(".VSIX")] // test case insensitivity
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