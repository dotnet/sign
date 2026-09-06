// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public sealed class ResolvedClickOnceAdjacentExecutableTests
    {
        private const string LauncherFileName = "Launcher.exe";
        private const string SetupFileName = "setup.exe";

        [Theory]
        [InlineData(SetupFileName, (int)ClickOnceAdjacentExecutableKind.Setup)]
        [InlineData("SETUP.EXE", (int)ClickOnceAdjacentExecutableKind.Setup)]
        [InlineData(LauncherFileName, (int)ClickOnceAdjacentExecutableKind.Launcher)]
        [InlineData("launcher.EXE", (int)ClickOnceAdjacentExecutableKind.Launcher)]
        public void Constructor_WithMatchingFileName_PreservesValues(
            string fileName,
            int kindValue)
        {
            FileInfo source = new(fileName);
            ClickOnceAdjacentExecutableKind kind =
                (ClickOnceAdjacentExecutableKind)kindValue;

            ResolvedClickOnceAdjacentExecutable executable = new(
                source,
                kind);

            Assert.Same(source, executable.Source);
            Assert.Equal(fileName, executable.TargetPath);
            Assert.Equal(kind, executable.Kind);
        }

        [Fact]
        public void Constructor_WithNullSource_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ResolvedClickOnceAdjacentExecutable(
                    source: null!,
                    ClickOnceAdjacentExecutableKind.Setup));
        }

        [Theory]
        [InlineData(SetupFileName, (int)ClickOnceAdjacentExecutableKind.Launcher)]
        [InlineData(LauncherFileName, (int)ClickOnceAdjacentExecutableKind.Setup)]
        public void Constructor_WithMismatchedFileName_ThrowsArgumentException(
            string fileName,
            int kindValue)
        {
            ClickOnceAdjacentExecutableKind kind =
                (ClickOnceAdjacentExecutableKind)kindValue;

            Assert.Throws<ArgumentException>(
                () => new ResolvedClickOnceAdjacentExecutable(
                    new FileInfo(fileName),
                    kind));
        }

        [Fact]
        public void Constructor_WithUndefinedKind_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ResolvedClickOnceAdjacentExecutable(
                    new FileInfo(SetupFileName),
                    kind: (ClickOnceAdjacentExecutableKind)(-1)));
        }
    }
}
