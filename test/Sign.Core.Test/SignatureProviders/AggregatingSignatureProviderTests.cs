// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Microsoft.Extensions.FileSystemGlobbing;
using Moq;

namespace Sign.Core.Test
{
    public partial class AggregatingSignatureProviderTests
    {
        private static readonly SignOptions _options = new(HashAlgorithmName.SHA256, new Uri("http://timestamp.test"));

        [Fact]
        public void Constructor_WhenSignatureProvidersIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSignatureProvider(
                    signatureProviders: null!,
                    Mock.Of<IDefaultSignatureProvider>(),
                    Mock.Of<IContainerProvider>(),
                    Mock.Of<IFileMetadataService>(),
                    Mock.Of<IMatcherFactory>()));

            Assert.Equal("signatureProviders", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenDefaultSignatureProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSignatureProvider(
                    Enumerable.Empty<ISignatureProvider>(),
                    defaultSignatureProvider: null!,
                    Mock.Of<IContainerProvider>(),
                    Mock.Of<IFileMetadataService>(),
                    Mock.Of<IMatcherFactory>()));

            Assert.Equal("defaultSignatureProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenContainerProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSignatureProvider(
                    Enumerable.Empty<ISignatureProvider>(),
                    Mock.Of<IDefaultSignatureProvider>(),
                    containerProvider: null!,
                    Mock.Of<IFileMetadataService>(),
                    Mock.Of<IMatcherFactory>()));

            Assert.Equal("containerProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenFileMetadataServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSignatureProvider(
                    Enumerable.Empty<ISignatureProvider>(),
                    Mock.Of<IDefaultSignatureProvider>(),
                    Mock.Of<IContainerProvider>(),
                    fileMetadataService: null!,
                    Mock.Of<IMatcherFactory>()));

            Assert.Equal("fileMetadataService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenMatcherFactoryIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSignatureProvider(
                    Enumerable.Empty<ISignatureProvider>(),
                    Mock.Of<IDefaultSignatureProvider>(),
                    Mock.Of<IContainerProvider>(),
                    Mock.Of<IFileMetadataService>(),
                    matcherFactory: null!));

            Assert.Equal("matcherFactory", exception.ParamName);
        }

        [Fact]
        public void CanSign_WhenFileIsNull_Throws()
        {
            AggregatingSignatureProvider provider = CreateProvider();

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => provider.CanSign(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Fact]
        public void CanSign_WhenSignatureProviderReturnsTrue_ReturnsTrue()
        {
            const string extension = ".xyz";

            Mock<ISignatureProvider> signatureProvider = new(MockBehavior.Strict);

            signatureProvider.Setup(x => x.CanSign(It.IsAny<FileInfo>()))
                .Returns(true);

            AggregatingSignatureProvider provider = CreateProvider(signatureProvider.Object);

            Assert.True(provider.CanSign(new FileInfo($"file{extension}")));

            signatureProvider.VerifyAll();
        }

        [Fact]
        public void CanSign_WhenSignatureProviderReturnsFalse_ReturnsFalse()
        {
            const string extension = ".xyz";

            Mock<ISignatureProvider> signatureProvider = new(MockBehavior.Strict);

            signatureProvider.Setup(x => x.CanSign(It.IsAny<FileInfo>()))
                .Returns(false);

            AggregatingSignatureProvider provider = CreateProvider(signatureProvider.Object);

            Assert.False(provider.CanSign(new FileInfo($"file{extension}")));

            signatureProvider.VerifyAll();
        }

        [Theory]
        [InlineData(".appxupload")]
        [InlineData(".msixupload")]
        [InlineData(".zip")]
        [InlineData(".ZIP")] // test case insensitivity
        public void CanSign_WhenExtensionIsSpecialCase_ReturnsTrue(string extension)
        {
            AggregatingSignatureProvider provider = CreateProvider();

            Assert.True(provider.CanSign(new FileInfo($"file{extension}")));
        }

        [Fact]
        public async Task SignAsync_WhenFilesIsNull_Throws()
        {
            AggregatingSignatureProvider provider = CreateProvider();

            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => provider.SignAsync(files: null!, _options));

            Assert.Equal("files", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenOptionsIsNull_Throws()
        {
            AggregatingSignatureProvider provider = CreateProvider();

            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => provider.SignAsync(Enumerable.Empty<FileInfo>(), options: null!));

            Assert.Equal("options", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenFilesIsEmpty_Returns()
        {
            AggregatingSignatureProvider provider = CreateProvider();

            await provider.SignAsync(Enumerable.Empty<FileInfo>(), _options);
        }

        private static AggregatingSignatureProvider CreateProvider(ISignatureProvider? signatureProvider = null)
        {
            IEnumerable<ISignatureProvider> signatureProviders;

            if (signatureProvider is null)
            {
                signatureProviders = Enumerable.Empty<ISignatureProvider>();
            }
            else
            {
                signatureProviders = new[] { signatureProvider };
            }

            Mock<IMatcherFactory> matcherFactory = new();

            matcherFactory.Setup(x => x.Create())
                .Returns(new Matcher(StringComparison.OrdinalIgnoreCase));

            return new AggregatingSignatureProvider(
                signatureProviders,
                Mock.Of<IDefaultSignatureProvider>(),
                Mock.Of<IContainerProvider>(),
                Mock.Of<IFileMetadataService>(),
                matcherFactory.Object);
        }
    }
}