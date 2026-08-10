// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using NSubstitute;

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
                    Substitute.For<ICertificateProvider>(),
                    Substitute.For<IDirectoryService>(),
                    Substitute.For<IFileMatcher>(),
                    Substitute.For<IMakeAppxCli>(),
                    Substitute.For<ILogger<IContainer>>()));

            Assert.Equal("appx", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxContainer(
                    appx: new FileInfo("a"),
                    certificateProvider: null!,
                    Substitute.For<IDirectoryService>(),
                    Substitute.For<IFileMatcher>(),
                    Substitute.For<IMakeAppxCli>(),
                    Substitute.For<ILogger<IContainer>>()));

            Assert.Equal("certificateProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenDirectoryServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxContainer(
                    appx: new FileInfo("a"),
                    Substitute.For<ICertificateProvider>(),
                    directoryService: null!,
                    Substitute.For<IFileMatcher>(),
                    Substitute.For<IMakeAppxCli>(),
                    Substitute.For<ILogger<IContainer>>()));

            Assert.Equal("directoryService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenFileMatcherIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxContainer(
                    appx: new FileInfo("a"),
                    Substitute.For<ICertificateProvider>(),
                    Substitute.For<IDirectoryService>(),
                    fileMatcher: null!,
                    Substitute.For<IMakeAppxCli>(),
                    Substitute.For<ILogger<IContainer>>()));

            Assert.Equal("fileMatcher", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenMakeAppxCliIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxContainer(
                    appx: new FileInfo("a"),
                    Substitute.For<ICertificateProvider>(),
                    Substitute.For<IDirectoryService>(),
                    Substitute.For<IFileMatcher>(),
                    makeAppxCli: null!,
                    Substitute.For<ILogger<IContainer>>()));

            Assert.Equal("makeAppxCli", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AppxContainer(
                    appx: new FileInfo("a"),
                    Substitute.For<ICertificateProvider>(),
                    Substitute.For<IDirectoryService>(),
                    Substitute.For<IFileMatcher>(),
                    Substitute.For<IMakeAppxCli>(),
                    logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }
    }
}