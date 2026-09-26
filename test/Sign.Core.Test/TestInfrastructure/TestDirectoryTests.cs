// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class TestDirectoryTests
    {
        [Fact]
        public void CreateFile_RootedPath_Throws()
        {
            using TestDirectory directory = new();

            Assert.Throws<ArgumentException>(
                () => directory.CreateFile(
                    relativePath: Path.Combine(
                        Path.GetPathRoot(directory.FullPath)!,
                        "outside.bin")));
        }

        [Fact]
        public void CreateFile_EscapingPath_Throws()
        {
            using TestDirectory directory = new();

            Assert.Throws<ArgumentException>(
                () => directory.CreateFile(
                    relativePath: Path.Combine("..", "outside.bin")));
        }
    }
}
