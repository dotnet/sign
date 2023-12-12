// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    public class AppxContainerTests
    {
        [Fact]
        public void Constructor_WhenAppxIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxContainer(
                    appx: null!,
                    Mock.Of<IKeyVaultService>(),
                    Mock.Of<IDirectoryService>(),
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<IMakeAppxCli>(),
                    Mock.Of<ILogger<IContainer>>()));

            Assert.Equal("appx", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenKeyVaultServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxContainer(
                    appx: new FileInfo("a"),
                    keyVaultService: null!,
                    Mock.Of<IDirectoryService>(),
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<IMakeAppxCli>(),
                    Mock.Of<ILogger<IContainer>>()));

            Assert.Equal("keyVaultService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenDirectoryServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxContainer(
                    appx: new FileInfo("a"),
                    Mock.Of<IKeyVaultService>(),
                    directoryService: null!,
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<IMakeAppxCli>(),
                    Mock.Of<ILogger<IContainer>>()));

            Assert.Equal("directoryService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenFileMatcherIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxContainer(
                    appx: new FileInfo("a"),
                    Mock.Of<IKeyVaultService>(),
                    Mock.Of<IDirectoryService>(),
                    fileMatcher: null!,
                    Mock.Of<IMakeAppxCli>(),
                    Mock.Of<ILogger<IContainer>>()));

            Assert.Equal("fileMatcher", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenMakeAppxCliIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxContainer(
                    appx: new FileInfo("a"),
                    Mock.Of<IKeyVaultService>(),
                    Mock.Of<IDirectoryService>(),
                    Mock.Of<IFileMatcher>(),
                    makeAppxCli: null!,
                    Mock.Of<ILogger<IContainer>>()));

            Assert.Equal("makeAppxCli", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxContainer(
                    appx: new FileInfo("a"),
                    Mock.Of<IKeyVaultService>(),
                    Mock.Of<IDirectoryService>(),
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<IMakeAppxCli>(),
                    logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }
    }
}