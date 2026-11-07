// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Sign.TestInfrastructure;

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
        public void PhysicalFile_NonexistentCaseOnlyPathVariant_IsNotEqual()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "SigningSourceIdentity",
                "File.bin");

            SigningSourceIdentity left =
                SigningSourceIdentity.PhysicalFile(path.ToUpperInvariant());
            SigningSourceIdentity right =
                SigningSourceIdentity.PhysicalFile(path.ToLowerInvariant());

            Assert.NotEqual(left, right);
        }

        [Fact]
        public void PhysicalFile_ExistingCaseOnlyPathAlias_IsEqual()
        {
            using TestDirectory directory = new();
            FileInfo file = directory.CreateFile(
                relativePath: "File.bin",
                contents: "content");
            SigningSourceIdentity left =
                SigningSourceIdentity.PhysicalFile(file.FullName);
            SigningSourceIdentity right =
                SigningSourceIdentity.PhysicalFile(
                    Path.Combine(
                        directory.FullPath,
                        "FILE.BIN"));

            Assert.Equal(left, right);
            Assert.Equal(left.GetHashCode(), right.GetHashCode());
        }

        [RequiresCaseSensitiveDirectoryFact]
        public void PhysicalFile_ExistingCaseOnlyDistinctFiles_AreNotEqual()
        {
            using TestDirectory directory = new();
            DirectoryInfo root = new(directory.FullPath);

            Assert.True(
                CaseSensitiveDirectory.TryEnable(root));
            FileInfo upper = directory.CreateFile(
                relativePath: "File.bin",
                contents: "upper");
            FileInfo lower = directory.CreateFile(
                relativePath: "file.bin",
                contents: "lower");
            SigningSourceIdentity left =
                SigningSourceIdentity.Capture(upper);
            SigningSourceIdentity right =
                SigningSourceIdentity.Capture(lower);

            Assert.NotEqual(left, right);
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
        public void PhysicalFile_DifferentHardLinkPaths_AreNotEqual()
        {
            using TestDirectory directory = new();
            FileInfo source = directory.CreateFile(
                relativePath: "source.bin",
                contents: "content");
            string aliasPath = Path.Combine(
                directory.FullPath,
                "alias.bin");
            HardLink.Create(aliasPath, source.FullName);

            SigningSourceIdentity sourceIdentity =
                SigningSourceIdentity.Capture(source);
            SigningSourceIdentity aliasIdentity =
                SigningSourceIdentity.Capture(new FileInfo(aliasPath));

            Assert.NotEqual(sourceIdentity, aliasIdentity);
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
        public void ContainerEntry_SamePathWithDifferentParents_IsNotEqual()
        {
            SigningSourceIdentity left =
                SigningSourceIdentity.ContainerEntry(
                    parent: SigningSourceIdentity.PhysicalFile(
                        path: @"C:\first.zip"),
                    entryPath: "payload/file.dll");
            SigningSourceIdentity right =
                SigningSourceIdentity.ContainerEntry(
                    parent: SigningSourceIdentity.PhysicalFile(
                        path: @"C:\second.zip"),
                    entryPath: "payload/file.dll");

            Assert.NotEqual(left, right);
        }

        [Fact]
        public void PhysicalFile_AndContainerEntry_AreNotEqual()
        {
            SigningSourceIdentity physical =
                SigningSourceIdentity.PhysicalFile(path: @"C:\file.bin");
            SigningSourceIdentity entry =
                SigningSourceIdentity.ContainerEntry(
                    parent: SigningSourceIdentity.PhysicalFile(
                        path: @"C:\container.zip"),
                    entryPath: "file.bin");

            Assert.NotEqual(physical, entry);
            Assert.NotEqual(entry, physical);
        }

        [Fact]
        public void ContainerEntry_EquivalentRecursiveIdentity_IsEqual()
        {
            using TestDirectory directory = new();
            FileInfo bundle = directory.CreateFile(
                relativePath: "Bundle.appxbundle",
                contents: "content");
            SigningSourceIdentity parentLeft =
                SigningSourceIdentity.PhysicalFile(
                    path: bundle.FullName);
            SigningSourceIdentity parentRight =
                SigningSourceIdentity.PhysicalFile(
                    path: Path.Combine(
                        directory.FullPath,
                        "BUNDLE.APPXBUNDLE"));
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

        [Fact]
        public void GetSibling_PhysicalFile_CombinesWithSourceDirectory()
        {
            SigningSourceIdentity source =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\publish\App.application");

            SigningSourceIdentity sibling = source.GetSibling(
                @"Application Files\App_1_0_0_0\App.exe");

            Assert.Equal(
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\publish\Application Files\App_1_0_0_0\App.exe"),
                sibling);
        }

        [Fact]
        public void GetSibling_PhysicalFileCaseOnlyVariant_IsEqual()
        {
            using TestDirectory directory = new();
            FileInfo application = directory.CreateFile(
                relativePath: "App.application",
                contents: "application");
            FileInfo executable = directory.CreateFile(
                relativePath: "App.exe",
                contents: "executable");
            SigningSourceIdentity source =
                SigningSourceIdentity.PhysicalFile(
                    path: application.FullName);

            SigningSourceIdentity sibling = source.GetSibling("app.EXE");
            SigningSourceIdentity expected =
                SigningSourceIdentity.PhysicalFile(
                    path: executable.FullName);

            Assert.Equal(expected, sibling);
            Assert.Equal(expected.GetHashCode(), sibling.GetHashCode());
        }

        [Fact]
        public void GetSibling_PhysicalFileRelativeSegmentsAndSeparators_AreNormalized()
        {
            SigningSourceIdentity source =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\publish\App.application");

            SigningSourceIdentity left = source.GetSibling(
                @"directory\.\child\..\file.bin");
            SigningSourceIdentity right = source.GetSibling(
                "directory/file.bin");

            Assert.Equal(left, right);
            Assert.Equal(
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\publish\directory\file.bin"),
                left);
        }

        [Fact]
        public void GetSibling_ContainerEntry_CombinesWithEntryDirectoryUnderSameParent()
        {
            SigningSourceIdentity parent =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\container.zip");
            SigningSourceIdentity source =
                SigningSourceIdentity.ContainerEntry(
                    parent: parent,
                    entryPath: "publish/App.application");

            SigningSourceIdentity sibling = source.GetSibling(
                @"Application Files\App.exe");
            SigningSourceIdentity expected =
                SigningSourceIdentity.ContainerEntry(
                    parent: parent,
                    entryPath: "publish/Application Files/App.exe");

            Assert.Equal(expected, sibling);
            Assert.Equal(expected.GetHashCode(), sibling.GetHashCode());
        }

        [Fact]
        public void GetSibling_ContainerEntryAtContainerRoot_CombinesWithoutDirectory()
        {
            SigningSourceIdentity parent =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\container.zip");
            SigningSourceIdentity source =
                SigningSourceIdentity.ContainerEntry(
                    parent: parent,
                    entryPath: "App.application");

            SigningSourceIdentity sibling = source.GetSibling("App.exe");

            Assert.Equal(
                SigningSourceIdentity.ContainerEntry(
                    parent: parent,
                    entryPath: "App.exe"),
                sibling);
        }

        [Fact]
        public void GetSibling_ContainerEntryCaseOnlyVariant_IsNotEqual()
        {
            SigningSourceIdentity parent =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\container.zip");
            SigningSourceIdentity source =
                SigningSourceIdentity.ContainerEntry(
                    parent: parent,
                    entryPath: "App.application");

            SigningSourceIdentity sibling = source.GetSibling("app.EXE");

            Assert.NotEqual(
                SigningSourceIdentity.ContainerEntry(
                    parent: parent,
                    entryPath: "App.exe"),
                sibling);
        }

        [Theory]
        [InlineData("../file.bin")]
        [InlineData("directory/../../file.bin")]
        [InlineData(@"C:\directory\file.bin")]
        [InlineData(@"\\server\share\file.bin")]
        public void GetSibling_InvalidRelativePath_Throws(string relativePath)
        {
            SigningSourceIdentity source =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\publish\App.application");

            Assert.Throws<ArgumentException>(
                () => source.GetSibling(relativePath));
        }

        [Fact]
        public void GetPhysicalRoot_PhysicalFile_ReturnsSelf()
        {
            SigningSourceIdentity file =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\publish\App.exe");

            Assert.Same(file, file.GetPhysicalRoot());
        }

        [Fact]
        public void GetPhysicalRoot_NestedContainerEntry_ReturnsOutermostFile()
        {
            SigningSourceIdentity container =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\container.zip");
            SigningSourceIdentity nestedContainer =
                SigningSourceIdentity.ContainerEntry(
                    parent: container,
                    entryPath: "nested.zip");
            SigningSourceIdentity entry =
                SigningSourceIdentity.ContainerEntry(
                    parent: nestedContainer,
                    entryPath: "App.exe");

            Assert.Same(container, nestedContainer.GetPhysicalRoot());
            Assert.Same(container, entry.GetPhysicalRoot());
        }

        [Theory]
        [InlineData(@"C:\publish\App.application", @"C:\publish\App.exe")]
        [InlineData(@"C:\publish\App.application", @"C:\publish\App.application")]
        [InlineData(@"C:\publish\App.application", @"C:\publish\Application Files\App_1_0\App.exe")]
        [InlineData(@"C:\publish\App.application", @"C:\PUBLISH\app.EXE")]
        [InlineData(@"C:\App.application", @"C:\publish\App.exe")]
        [InlineData(@"\\server\share\App.application", @"\\server\share\publish\App.exe")]
        [InlineData(@"\\server\share\publish\App.application", @"\\server\share\publish\App.exe")]
        public void IsInDirectoryTreeOf_FileInDirectoryTree_ReturnsTrue(
            string filePath,
            string candidatePath)
        {
            SigningSourceIdentity file =
                SigningSourceIdentity.PhysicalFile(filePath);
            SigningSourceIdentity candidate =
                SigningSourceIdentity.PhysicalFile(candidatePath);

            Assert.True(candidate.IsInDirectoryTreeOf(file));
        }

        [Theory]
        [InlineData(@"C:\publish\App.application", @"C:\App.exe")]
        [InlineData(@"C:\publish\App.application", @"C:\publish2\App.exe")]
        [InlineData(@"C:\publish\App.application", @"C:\pub\App.exe")]
        [InlineData(@"C:\publish\App.application", @"D:\publish\App.exe")]
        [InlineData(@"C:\publish\nested\App.application", @"C:\publish\App.exe")]
        [InlineData(@"\\server\share\App.application", @"\\server\share2\App.exe")]
        [InlineData(@"\\server\share\App.application", @"\\server2\share\App.exe")]
        public void IsInDirectoryTreeOf_FileOutsideDirectoryTree_ReturnsFalse(
            string filePath,
            string candidatePath)
        {
            SigningSourceIdentity file =
                SigningSourceIdentity.PhysicalFile(filePath);
            SigningSourceIdentity candidate =
                SigningSourceIdentity.PhysicalFile(candidatePath);

            Assert.False(candidate.IsInDirectoryTreeOf(file));
        }

        [Fact]
        public void IsInDirectoryTreeOf_ContainerEntry_UsesContainerPath()
        {
            SigningSourceIdentity application =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\publish\App.application");
            SigningSourceIdentity inside =
                SigningSourceIdentity.ContainerEntry(
                    parent: SigningSourceIdentity.PhysicalFile(
                        path: @"C:\publish\sub\container.zip"),
                    entryPath: "App.exe");
            SigningSourceIdentity outside =
                SigningSourceIdentity.ContainerEntry(
                    parent: SigningSourceIdentity.PhysicalFile(
                        path: @"C:\container.zip"),
                    entryPath: @"publish\App.exe");

            Assert.True(inside.IsInDirectoryTreeOf(application));
            Assert.False(outside.IsInDirectoryTreeOf(application));
        }

        [Fact]
        public void IsInDirectoryTreeOf_ContainerEntryFile_UsesContainerDirectory()
        {
            SigningSourceIdentity application =
                SigningSourceIdentity.ContainerEntry(
                    parent: SigningSourceIdentity.PhysicalFile(
                        path: @"C:\publish\container.zip"),
                    entryPath: @"nested\App.application");
            SigningSourceIdentity candidate =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\publish\App.exe");

            Assert.True(candidate.IsInDirectoryTreeOf(application));
        }

        [Theory]
        [InlineData("App.exe")]
        [InlineData(@"Application Files\App_1_0\App.exe")]
        [InlineData(@"Application Files\.\App_1_0\..\App_1_0\App.exe")]
        [InlineData(@"a.\b..\App.exe")]
        [InlineData(@"...\App.exe")]
        [InlineData(@"Application Files\...")]
        public void IsInDirectoryTreeOf_Sibling_ReturnsTrue(string relativePath)
        {
            SigningSourceIdentity application =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\publish\App.application");

            SigningSourceIdentity sibling =
                application.GetSibling(relativePath);

            Assert.True(sibling.IsInDirectoryTreeOf(application));
        }

        [Fact]
        public void IsInDirectoryTreeOf_Null_Throws()
        {
            SigningSourceIdentity file =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\publish\App.exe");

            Assert.Throws<ArgumentNullException>(
                () => file.IsInDirectoryTreeOf(null!));
        }
    }
}
