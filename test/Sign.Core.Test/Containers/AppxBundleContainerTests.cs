// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Sign.Core.Test
{
    public class AppxBundleContainerTests
    {
        [Fact]
        public void Constructor_WhenAppxIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxBundleContainer(
                    appxBundle: null!,
                    Substitute.For<IDirectoryService>(),
                    Substitute.For<IFileMatcher>(),
                    Substitute.For<IMakeAppxCli>(),
                    Substitute.For<ILogger>()));

            Assert.Equal("appxBundle", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenDirectoryServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxBundleContainer(
                    new FileInfo("a"),
                    directoryService: null!,
                    Substitute.For<IFileMatcher>(),
                    Substitute.For<IMakeAppxCli>(),
                    Substitute.For<ILogger>()));

            Assert.Equal("directoryService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenFileMatcherIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxBundleContainer(
                    new FileInfo("a"),
                    Substitute.For<IDirectoryService>(),
                    fileMatcher: null!,
                    Substitute.For<IMakeAppxCli>(),
                    Substitute.For<ILogger>()));

            Assert.Equal("fileMatcher", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenMakeAppxCliIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxBundleContainer(
                    new FileInfo("a"),
                    Substitute.For<IDirectoryService>(),
                    Substitute.For<IFileMatcher>(),
                    makeAppxCli: null!,
                    Substitute.For<ILogger>()));

            Assert.Equal("makeAppxCli", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxBundleContainer(
                    new FileInfo("a"),
                    Substitute.For<IDirectoryService>(),
                    Substitute.For<IFileMatcher>(),
                    Substitute.For<IMakeAppxCli>(),
                    logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }
    }
}