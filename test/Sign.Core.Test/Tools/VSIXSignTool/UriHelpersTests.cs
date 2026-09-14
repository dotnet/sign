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
        [InlineData("package:///nested/ab%3Fc.txt", "nested/ab%3Fc.txt")]
        [InlineData("package:///ab%25c.txt", "ab%25c.txt")]
        [InlineData("package:///caf%C3%A9.txt", "caf%C3%A9.txt")]
        [InlineData("nested/ordinary.txt", "nested/ordinary.txt")]
        [InlineData("nested/ab%3Fc.txt", "nested/ab%3Fc.txt")]
        public void ShouldHandlePackagePathForRelativeUris(string uri, string expected)
        {
            var part = new Uri(uri, UriKind.RelativeOrAbsolute);
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
        [InlineData("package:///ab%23c.txt", "/ab%23c.txt")]
        [InlineData("package:///nested/ab%3Fc.txt", "/nested/ab%3Fc.txt")]
        [InlineData("package:///ab%25c.txt", "/ab%25c.txt")]
        [InlineData("package:///caf%C3%A9.txt", "/caf%C3%A9.txt")]
        [InlineData("package:///ab%23c.txt?ContentType=text/plain", "/ab%23c.txt?ContentType=text/plain")]
        [InlineData("nested/ordinary.txt", "nested/ordinary.txt")]
        [InlineData("nested/ab%3Fc.txt", "nested/ab%3Fc.txt")]
        public void ShouldHandleReferencePathForRelativeUris(string uri, string expected)
        {
            var part = new Uri(uri, UriKind.RelativeOrAbsolute);
            var packagePath = part.ToQualifiedPath();
            Assert.Equal(expected, packagePath);
        }

        [Theory]
        [InlineData("package:///file.bin", "/file.bin")]
        [InlineData("package:///ab%23c.txt?ContentType=text/plain", "/ab%23c.txt?ContentType=text/plain")]
        [InlineData("package:///nested/ab%3Fc.txt", "/nested/ab%3Fc.txt")]
        public void ShouldHandleQualifiedUri(string uri, string expected)
        {
            var part = new Uri(uri, UriKind.Absolute);
            var qualifiedUri = part.ToQualifiedUri();

            Assert.Equal(expected, qualifiedUri.OriginalString);
        }

        [Fact]
        public void ShouldTreatUnescapedNumberSignAsUriFragment()
        {
            // OPC part names must encode a literal '#'; an unescaped '#' starts the URI fragment.
            var part = new Uri("package:///ab#c.txt", UriKind.Absolute);

            Assert.Equal("ab", part.ToPackagePath());
            Assert.Equal("/ab", part.ToQualifiedPath());
        }

        [Fact]
        public void ShouldPreserveEncodedRelationshipTarget()
        {
            var relationships = new OpcRelationships(
                new Uri("package:///_rels/.rels", UriKind.Absolute),
                isReadOnly: false);
            relationships.Add(
                new OpcRelationship(
                    new Uri("/nested/ab%23c.txt", UriKind.Relative),
                    new Uri("https://example.test/relationship", UriKind.Absolute)));

            string? target = relationships.ToXml().Root?
                .Elements()
                .Single()
                .Attribute("Target")?
                .Value;

            Assert.Equal("/nested/ab%23c.txt", target);
        }
    }
}
