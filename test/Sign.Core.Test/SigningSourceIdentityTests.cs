// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class SigningSourceIdentityTests
    {
        [Fact]
        public void PhysicalFile_SameCanonicalPath_IsEqual()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "directory",
                "..",
                "file.bin");

            SigningSourceIdentity left =
                SigningSourceIdentity.PhysicalFile(path);
            SigningSourceIdentity right = SigningSourceIdentity.PhysicalFile(
                Path.Combine(Path.GetTempPath(), "file.bin"));

            Assert.Equal(left, right);
            Assert.Equal(left.GetHashCode(), right.GetHashCode());
        }

        [Fact]
        public void PhysicalFile_CaseOnlyPathVariant_IsEqual()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "SigningSourceIdentity",
                "File.bin");

            SigningSourceIdentity left =
                SigningSourceIdentity.PhysicalFile(path.ToUpperInvariant());
            SigningSourceIdentity right =
                SigningSourceIdentity.PhysicalFile(path.ToLowerInvariant());

            Assert.Equal(left, right);
            Assert.Equal(left.GetHashCode(), right.GetHashCode());
        }

        [Fact]
        public void PhysicalFile_DifferentPaths_AreNotEqual()
        {
            SigningSourceIdentity left =
                SigningSourceIdentity.PhysicalFile(
                    Path.Combine(Path.GetTempPath(), "first.bin"));
            SigningSourceIdentity right =
                SigningSourceIdentity.PhysicalFile(
                    Path.Combine(Path.GetTempPath(), "second.bin"));

            Assert.NotEqual(left, right);
        }

        [Fact]
        public void ContainerEntry_SeparatorsAndRelativeSegments_AreNormalized()
        {
            SigningSourceIdentity parent =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\container.zip");

            SigningSourceIdentity left =
                SigningSourceIdentity.ContainerEntry(
                    parent: parent,
                    entryPath: @"directory\\.\child\..\file.bin");
            SigningSourceIdentity right =
                SigningSourceIdentity.ContainerEntry(
                    parent: parent,
                    entryPath: "directory/file.bin");

            Assert.Equal(left, right);
            Assert.Equal(left.GetHashCode(), right.GetHashCode());
        }

        [Fact]
        public void ContainerEntry_CaseOnlyVariant_IsNotEqual()
        {
            SigningSourceIdentity parent =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\container.zip");

            SigningSourceIdentity left =
                SigningSourceIdentity.ContainerEntry(
                    parent: parent,
                    entryPath: "File.bin");
            SigningSourceIdentity right =
                SigningSourceIdentity.ContainerEntry(
                    parent: parent,
                    entryPath: "file.bin");

            Assert.NotEqual(left, right);
        }

        [Fact]
        public void ContainerEntry_EquivalentRecursiveIdentity_IsEqual()
        {
            SigningSourceIdentity parentLeft =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\bundle.appxbundle");
            SigningSourceIdentity parentRight =
                SigningSourceIdentity.PhysicalFile(
                    path: @"c:\BUNDLE.appxbundle");
            SigningSourceIdentity nestedLeft =
                SigningSourceIdentity.ContainerEntry(
                    SigningSourceIdentity.ContainerEntry(
                        parent: parentLeft,
                        entryPath: "application.appx"),
                    entryPath: "payload/file.dll");
            SigningSourceIdentity nestedRight =
                SigningSourceIdentity.ContainerEntry(
                    SigningSourceIdentity.ContainerEntry(
                        parent: parentRight,
                        entryPath: @".\application.appx"),
                    entryPath: @"payload\file.dll");

            Assert.Equal(nestedLeft, nestedRight);
            Assert.Equal(
                nestedLeft.GetHashCode(),
                nestedRight.GetHashCode());
        }

        [Theory]
        [InlineData("../file.bin")]
        [InlineData("directory/../../file.bin")]
        [InlineData(".")]
        [InlineData("directory/..")]
        [InlineData("///")]
        [InlineData("/directory/file.bin")]
        [InlineData(@"\directory\file.bin")]
        [InlineData(@"C:\directory\file.bin")]
        [InlineData(@"\\server\share\file.bin")]
        public void ContainerEntry_InvalidPath_Throws(string path)
        {
            SigningSourceIdentity parent =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\container.zip");

            Assert.Throws<ArgumentException>(
                () => SigningSourceIdentity.ContainerEntry(
                    parent: parent,
                    entryPath: path));
        }

        [Fact]
        public void Capture_SourceFileIsReplaced_PreservesCapturedPathIdentity()
        {
            using TestDirectory directory = new();
            string path = Path.Combine(directory.FullPath, "source.bin");
            string replacementPath = Path.Combine(
                directory.FullPath,
                "replacement.bin");
            File.WriteAllText(path: path, contents: "original");
            File.WriteAllText(
                path: replacementPath,
                contents: "replacement");
            SigningSourceIdentity original =
                SigningSourceIdentity.Capture(new FileInfo(path));

            int originalHashCode = original.GetHashCode();
            File.Move(replacementPath, path, overwrite: true);
            SigningSourceIdentity replacement =
                SigningSourceIdentity.Capture(new FileInfo(path));

            Assert.Equal(originalHashCode, original.GetHashCode());
            Assert.Equal(original, replacement);
        }

    }
}
