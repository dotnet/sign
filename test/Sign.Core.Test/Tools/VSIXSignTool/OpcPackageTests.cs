// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class OpcPackageTests : IDisposable
    {
        private static readonly string SamplePackage = Path.Combine(".", "TestAssets", "VSIXSamples", "OpenVsixSignToolTest.vsix");
        private static readonly string SamplePackageSigned = Path.Combine(".", "TestAssets", "VSIXSamples", "OpenVsixSignToolTest-Signed.vsix");
        private readonly List<string> _shadowFiles = new List<string>();

        [Fact]
        public void ShouldOpenAndDisposeAPackageAndDisposeIsIdempotent()
        {
            var package = OpcPackage.Open(SamplePackage);
            package.Dispose();
            package.Dispose();
        }

        [Fact]
        public void ShouldReadContentTypes()
        {
            using (var package = OpcPackage.Open(SamplePackage))
            {
                Assert.Equal(4, package.ContentTypes.Count);
                var first = package.ContentTypes[0];
                Assert.Equal("vsixmanifest", first.Extension);
                Assert.Equal("text/xml", first.ContentType);
                Assert.Equal(OpcContentTypeMode.Default, first.Mode);
            }
        }

        [Fact]
        public void ShouldNotAllowUpdatingContentTypesInReadOnly()
        {
            using (var package = OpcPackage.Open(SamplePackage))
            {
                var newItem = new OpcContentType("test", "test", OpcContentTypeMode.Default);
                var contentTypes = package.ContentTypes;
                Assert.Throws<InvalidOperationException>(() => contentTypes.Add(newItem));
            }
        }

        [Fact]
        public void ShouldAllowUpdatingContentType()
        {
            int initialCount;
            string shadowPath;
            using (var package = ShadowCopyPackage(SamplePackage, out shadowPath, OpcPackageFileMode.ReadWrite))
            {
                initialCount = package.ContentTypes.Count;
                var newItem = new OpcContentType("test", "application/test", OpcContentTypeMode.Default);
                package.ContentTypes.Add(newItem);
            }
            using (var reopenedPackage = OpcPackage.Open(shadowPath))
            {
                Assert.Equal(initialCount + 1, reopenedPackage.ContentTypes.Count);
            }
        }

        [Fact]
        public void ShouldAllowUpdatingRelationships()
        {
            int initialCount;
            string shadowPath;
            using (var package = ShadowCopyPackage(SamplePackage, out shadowPath, OpcPackageFileMode.ReadWrite))
            {
                initialCount = package.Relationships.Count;
                var newItem = new OpcRelationship(new Uri("/test", UriKind.RelativeOrAbsolute), new Uri("/test", UriKind.RelativeOrAbsolute));
                package.Relationships.Add(newItem);
                Assert.True(newItem.Id != null && newItem.Id.Length == 9);
            }
            using (var reopenedPackage = OpcPackage.Open(shadowPath))
            {
                Assert.Equal(initialCount + 1, reopenedPackage.Relationships.Count);
            }
        }

        [Fact]
        public void ShouldRemovePart()
        {
            using (var package = ShadowCopyPackage(SamplePackage, out _, OpcPackageFileMode.ReadWrite))
            {
                var partToRemove = new Uri("/extension.vsixmanifest", UriKind.Relative);
                var part = package.GetPart(partToRemove);
                Assert.NotNull(part);
                package.RemovePart(part);
                Assert.Null(package.GetPart(partToRemove));
            }
        }


        [Fact]
        public void ShouldRemoveRelationshipsForRemovedPartWhereRelationshipIsMaterialized()
        {
            string path;
            using (var package = ShadowCopyPackage(SamplePackage, out path, OpcPackageFileMode.ReadWrite))
            {
                var partToRemove = new Uri("/extension.vsixmanifest", UriKind.Relative);
                var part = package.GetPart(partToRemove);
                part!.Relationships.Add(new OpcRelationship(new Uri("/test", UriKind.Relative), new Uri("http://test.com", UriKind.Absolute)));
            }
            using (var package = OpcPackage.Open(path, OpcPackageFileMode.ReadWrite))
            {
                var relationshipPartUri = new Uri("/_rels/extension.vsixmanifest.rels", UriKind.Relative);
                Assert.NotNull(package.GetPart(relationshipPartUri));
                var partToRemove = new Uri("/extension.vsixmanifest", UriKind.Relative);
                var part = package.GetPart(partToRemove);
                package.RemovePart(part!);
                Assert.False(package.HasPart(relationshipPartUri));
                Assert.Null(package.GetPart(relationshipPartUri));
            }
        }

        [Fact]
        public void ShouldRemoveRelationshipsForRemovedPartWhereRelationshipIsNotMaterialized()
        {
            string path;
            using (var package = ShadowCopyPackage(SamplePackage, out path, OpcPackageFileMode.ReadWrite))
            {
                var partToRemove = new Uri("/extension.vsixmanifest", UriKind.Relative);
                var part = package.GetPart(partToRemove);
                part!.Relationships.Add(new OpcRelationship(new Uri("/test", UriKind.Relative), new Uri("http://test.com", UriKind.Absolute)));
                package.RemovePart(part);
            }
            using (var package = OpcPackage.Open(path))
            {
                var relationshipPartUri = new Uri("/_rels/extension.vsixmanifest.rels", UriKind.Relative);
                Assert.False(package.HasPart(relationshipPartUri));
                Assert.Null(package.GetPart(relationshipPartUri));
            }
        }

        [Fact]
        public void ShouldEnumerateAllParts()
        {
            using (var package = OpcPackage.Open(SamplePackage))
            {
                var parts = package.GetParts().ToArray();
                Assert.Equal(2, parts.Length);
            }
        }

        [Fact]
        public void ShouldCreateSignatureBuilder()
        {
            using (var package = OpcPackage.Open(SamplePackage))
            {
                var builder = package.CreateSignatureBuilder();
                foreach (var part in package.GetParts())
                {
                    builder.EnqueuePart(part);
                    Assert.True(builder.DequeuePart(part));
                }
            }
        }

        [Theory]
        [InlineData("extension.vsixmanifest")]
        [InlineData("/extension.vsixmanifest")]
        [InlineData("package:///extension.vsixmanifest")]
        public void ShouldOpenSinglePartByRelativeUri(string uri)
        {
            var partUri = new Uri(uri, UriKind.RelativeOrAbsolute);
            using (var package = OpcPackage.Open(SamplePackage))
            {
                Assert.NotNull(package.GetPart(partUri));
            }
        }

        [Fact]
        public void ShouldReturnEmptyEnumerableForNoSignatureOriginRelationship()
        {
            using (var package = OpcPackage.Open(SamplePackage))
            {
                Assert.Empty(package.GetSignatures());
            }
        }

        [Fact]
        public void ShouldReturnSignatureForSignedPackage()
        {
            using (var package = OpcPackage.Open(SamplePackageSigned))
            {
                Assert.NotEmpty(package.GetSignatures());
            }
        }

        private OpcPackage ShadowCopyPackage(string packagePath, out string path, OpcPackageFileMode mode = OpcPackageFileMode.Read)
        {
            var temp = Path.GetTempFileName();
            _shadowFiles.Add(temp);
            File.Copy(packagePath, temp, true);
            path = temp;
            return OpcPackage.Open(temp, mode);
        }

        public void Dispose()
        {
            void CleanUpShadows()
            {
                _shadowFiles.ForEach(File.Delete);
            }
            CleanUpShadows();
        }
    }
}
