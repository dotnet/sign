// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class UriHelpersTests
    {
        [Theory]
        [InlineData("package:///file.bin", "file.bin")]
        [InlineData("package:/file.bin", "file.bin")]
        [InlineData("package:///sub/file.bin", "sub/file.bin")]
        [InlineData("package:/sub/file.bin", "sub/file.bin")]
        [InlineData("package:///sub/file.bin?query=string", "sub/file.bin")]
        [InlineData("package:/sub/file.bin?query=string", "sub/file.bin")]
        [InlineData("package:///ab%23c.txt", "ab%23c.txt")]
        [InlineData("package:///ab%3Fc.txt", "ab%3Fc.txt")]
        [InlineData("package:///folder/ab%3fc.txt", "folder/ab%3fc.txt")]
        public void ShouldHandlePackagePathForRelativeUris(string uri, string expected)
        {
            var part = new Uri(uri, UriKind.Absolute);
            var packagePath = part.ToPackagePath();
            Assert.Equal(expected, packagePath);
        }

        [Theory]
        [InlineData("package:///file.bin", "/file.bin")]
        [InlineData("package:/file.bin", "/file.bin")]
        [InlineData("package:///sub/file.bin", "/sub/file.bin")]
        [InlineData("package:/sub/file.bin", "/sub/file.bin")]
        [InlineData("package:///sub/file.bin?query=string", "/sub/file.bin?query=string")]
        [InlineData("package:/sub/file.bin?query=string", "/sub/file.bin?query=string")]
        [InlineData("package:///ab%23c.txt?query=string", "/ab%23c.txt?query=string")]
        [InlineData("package:///ab%3Fc.txt?query=string", "/ab%3Fc.txt?query=string")]
        [InlineData("package:///ab%2Fc.txt?query=string", "/ab%2Fc.txt?query=string")]
        public void ShouldHandleReferencePathForRelativeUris(string uri, string expected)
        {
            var part = new Uri(uri, UriKind.Absolute);
            var packagePath = part.ToQualifiedPath();
            Assert.Equal(expected, packagePath);
        }
    }
}
