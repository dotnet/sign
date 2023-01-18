// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    public class ContainerProviderTests
    {
        private readonly ContainerProvider _provider;

        public ContainerProviderTests()
        {
            _provider = new ContainerProvider(
                Mock.Of<IKeyVaultService>(),
                Mock.Of<IDirectoryService>(),
                Mock.Of<IFileMatcher>(),
                Mock.Of<IMakeAppxCli>(),
                Mock.Of<ILogger<IDirectoryService>>());
        }

        [Fact]
        public void Constructor_WhenKeyVaultServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ContainerProvider(
                    keyVaultService: null!,
                    Mock.Of<IDirectoryService>(),
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<IMakeAppxCli>(),
                    Mock.Of<ILogger<IDirectoryService>>()));

            Assert.Equal("keyVaultService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenDirectoryServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ContainerProvider(
                    Mock.Of<IKeyVaultService>(),
                    directoryService: null!,
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<IMakeAppxCli>(),
                    Mock.Of<ILogger<IDirectoryService>>()));

            Assert.Equal("directoryService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenFileMatcherIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ContainerProvider(
                    Mock.Of<IKeyVaultService>(),
                    Mock.Of<IDirectoryService>(),
                    fileMatcher: null!,
                    Mock.Of<IMakeAppxCli>(),
                    Mock.Of<ILogger<IDirectoryService>>()));

            Assert.Equal("fileMatcher", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenMakeAppxCliIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ContainerProvider(
                    Mock.Of<IKeyVaultService>(),
                    Mock.Of<IDirectoryService>(),
                    Mock.Of<IFileMatcher>(),
                    makeAppxCli: null!,
                    Mock.Of<ILogger<IDirectoryService>>()));

            Assert.Equal("makeAppxCli", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ContainerProvider(
                    Mock.Of<IKeyVaultService>(),
                    Mock.Of<IDirectoryService>(),
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<IMakeAppxCli>(),
                    logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public void IsAppxBundleContainer_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _provider.IsAppxBundleContainer(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Theory]
        [InlineData(".dll")]
        [InlineData(".msİxbundle")] // Turkish İ (U+0130)
        [InlineData(".msıxbundle")] // Turkish ı (U+0131)
        public void IsAppxBundleContainer_WhenFileExtensionDoesNotMatch_ReturnsFalse(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.False(_provider.IsAppxBundleContainer(file));
        }

        [Theory]
        [InlineData(".appxbundle")]
        [InlineData(".eappxbundle")]
        [InlineData(".emsixbundle")]
        [InlineData(".msixbundle")]
        [InlineData(".MSIXBUNDLE")] // test case insensitivity
        public void IsAppxBundleContainer_WhenFileExtensionMatches_ReturnsTrue(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.True(_provider.IsAppxBundleContainer(file));
        }

        [Fact]
        public void IsAppxContainer_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _provider.IsAppxContainer(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Theory]
        [InlineData(".dll")]
        [InlineData(".msİx")] // Turkish İ (U+0130)
        [InlineData(".msıx")] // Turkish ı (U+0131)
        public void IsAppxContainer_WhenFileExtensionDoesNotMatch_ReturnsFalse(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.False(_provider.IsAppxContainer(file));
        }

        [Theory]
        [InlineData(".appx")]
        [InlineData(".eappx")]
        [InlineData(".emsix")]
        [InlineData(".msix")]
        [InlineData(".MSIX")] // test case insensitivity
        public void IsAppxContainer_WhenFileExtensionMatches_ReturnsTrue(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.True(_provider.IsAppxContainer(file));
        }

        [Fact]
        public void IsNuGetContainer_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _provider.IsNuGetContainer(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Theory]
        [InlineData(".zip")]
        public void IsNuGetContainer_WhenFileExtensionDoesNotMatch_ReturnsFalse(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.False(_provider.IsNuGetContainer(file));
        }

        [Theory]
        [InlineData(".nupkg")]
        [InlineData(".snupkg")]
        [InlineData(".NuPkg")] // test case insensitivity
        public void IsNuGetContainer_WhenFileExtensionMatches_ReturnsTrue(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.True(_provider.IsNuGetContainer(file));
        }

        [Fact]
        public void IsZipContainer_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _provider.IsZipContainer(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Theory]
        [InlineData(".dll")]
        [InlineData(".msİx")] // Turkish İ (U+0130)
        [InlineData(".msıx")] // Turkish ı (U+0131)
        public void IsZipContainer_WhenFileExtensionDoesNotMatch_ReturnsFalse(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.False(_provider.IsZipContainer(file));
        }

        [Theory]
        [InlineData(".appxupload")]
        [InlineData(".clickonce")]
        [InlineData(".msixupload")]
        [InlineData(".vsix")]
        [InlineData(".zip")]
        [InlineData(".ZIP")] // test case insensitivity
        public void IsZipContainer_WhenFileExtensionMatches_ReturnsTrue(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.True(_provider.IsZipContainer(file));
        }

        [Fact]
        public void GetContainer_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _provider.GetContainer(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Fact]
        public void GetContainer_WhenFileExtensionDoesNotMatch_ReturnsFalse()
        {
            FileInfo file = new("file.dll");

            Assert.Null(_provider.GetContainer(file));
        }

        [Fact]
        public void GetContainer_WhenFileExtensionMatchesZip_ReturnsContainer()
        {
            FileInfo file = new("file.zip");
            IContainer? container = _provider.GetContainer(file);

            Assert.IsType<ZipContainer>(container);
        }

        [Fact]
        public void GetContainer_WhenFileExtensionMatchesAppx_ReturnsContainer()
        {
            FileInfo file = new("file.appx");
            IContainer? container = _provider.GetContainer(file);

            Assert.IsType<AppxContainer>(container);
        }

        [Fact]
        public void GetContainer_WhenFileExtensionMatchesAppxBundle_ReturnsContainer()
        {
            FileInfo file = new("file.appxbundle");
            IContainer? container = _provider.GetContainer(file);

            Assert.IsType<AppxBundleContainer>(container);
        }
    }
}