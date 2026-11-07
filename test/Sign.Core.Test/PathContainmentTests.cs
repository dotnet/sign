// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class PathContainmentTests
    {
        [Theory]
        [InlineData(@"C:\", @"C:\")]
        [InlineData(@"C:\publish", @"C:\publish\")]
        [InlineData(@"C:\publish\", @"C:\publish\")]
        [InlineData(@"\\server\share", @"\\server\share\")]
        public void EnsureTrailingSeparator_Always_ReturnsPathWithOneTrailingSeparator(
            string path,
            string expectedPath)
        {
            Assert.Equal(
                expectedPath,
                PathContainment.EnsureTrailingSeparator(path));
        }

        [Theory]
        [InlineData(@"C:\", @"C:\")]
        [InlineData(@"C:\publish", @"C:\publish\")]
        [InlineData(@"\\server\share", @"\\server\share\")]
        [InlineData(@"\\server\share\", @"\\server\share\")]
        public void GetFullDirectoryPath_Always_ReturnsFullPathWithOneTrailingSeparator(
            string directoryPath,
            string expectedPath)
        {
            Assert.Equal(
                expectedPath,
                PathContainment.GetFullDirectoryPath(directoryPath));
        }

        [Theory]
        [InlineData(@"C:\", @"C:\publish\App.application")]
        [InlineData(@"C:\publish\", @"C:\publish\sub\x")]
        [InlineData(@"C:\Publish\", @"c:\publish\sub\x")]
        [InlineData(@"\\server\share\", @"\\server\share\publish\x")]
        public void IsContained_PathUnderRoot_ReturnsTrue(
            string rootPath,
            string fullPath)
        {
            Assert.True(PathContainment.IsContained(rootPath, fullPath));
        }

        [Theory]
        [InlineData(@"C:\pub\", @"C:\publish\x")]
        [InlineData(@"C:\publish\", @"D:\publish\x")]
        public void IsContained_PathNotUnderRoot_ReturnsFalse(
            string rootPath,
            string fullPath)
        {
            Assert.False(PathContainment.IsContained(rootPath, fullPath));
        }
    }
}
