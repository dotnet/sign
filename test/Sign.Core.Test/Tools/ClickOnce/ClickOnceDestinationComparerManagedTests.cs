// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.AccessControl;
using System.Security.Principal;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed class ClickOnceDestinationComparerManagedTests
    {
        [Fact]
        public void Equals_WhenDriveRootHasExtendedPrefixOrDifferentCase_PreservesEqualityAndHash()
        {
            string root = Path.GetPathRoot(Path.GetTempPath())!;
            AssertEquivalent(root, root.ToLowerInvariant());
            AssertEquivalent(root, @"\\?\" + root);
        }

        [Fact]
        public void Equals_WhenUncRootHasExtendedPrefix_PreservesUncRootAndHash()
        {
            AssertEquivalent(@"\\destination-test-server\share\", @"\\?\UNC\destination-test-server\share\");
            Assert.False(ClickOnceDestinationComparer.Instance.Equals(
                @"\\destination-test-server\share\", @"\\destination-test-server\other\"));
        }

        [Fact]
        public void Equals_WhenPathContainsRelativeSegments_NormalizesBeforeComparison()
        {
            using TestDirectory directory = new();
            string path = Path.Combine(directory.FullPath, "payload.dll");
            string relative = Path.GetRelativePath(Environment.CurrentDirectory, path);

            AssertEquivalent(path, relative);
            AssertEquivalent(path, Path.Combine(directory.FullPath, "missing", "..", ".", "payload.dll"));
            AssertEquivalent(path, @"\\?\" + path);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Equals_WhenExistingParentHasCaseAlias_HandlesExistingAndMissingLeaves(bool missing)
        {
            using TestDirectory directory = new();
            DirectoryInfo parent = Directory.CreateDirectory(Path.Combine(directory.FullPath, "Parent"));
            string first = Path.Combine(parent.FullName, missing ? @"Output\payload.dll" : "payload.dll");
            if (!missing)
            {
                File.WriteAllText(first, "payload");
            }
            string second = Path.Combine(directory.FullPath, "PARENT",
                missing ? @"OUTPUT\PAYLOAD.DLL" : "PAYLOAD.DLL");

            AssertEquivalent(first, second);
            if (missing)
            {
                Assert.False(Directory.Exists(Path.Combine(parent.FullName, "Output")));
            }
        }

        [RequiresShortDestinationPathFact]
        public void Equals_WhenParentHasActualShortAlias_PreservesExistingAndMissingDestinationHashes()
        {
            using TestDirectory directory = new();
            DirectoryInfo parent = Directory.CreateDirectory(
                Path.Combine(directory.FullPath, "LongDestinationParent"));
            string shortParent = RequiresShortDestinationPathFactAttribute.GetShortPath(parent.FullName);
            Assert.False(string.Equals(parent.FullName, shortParent, StringComparison.OrdinalIgnoreCase));
            File.WriteAllText(Path.Combine(parent.FullName, "payload.dll"), "payload");

            AssertEquivalent(Path.Combine(parent.FullName, "payload.dll"), Path.Combine(shortParent, "payload.dll"));
            AssertEquivalent(Path.Combine(parent.FullName, "Missing", "payload.dll"),
                Path.Combine(shortParent, "Missing", "payload.dll"));
        }

        [Fact]
        public void Equals_WhenHardLinksHaveDifferentNames_KeepsLogicalDestinationsDistinct()
        {
            using TestDirectory directory = new();
            FileInfo source = directory.CreateFile("payload.dll", "payload");
            string alias = Path.Combine(directory.FullPath, "alias.dll");
            HardLink.Create(alias, source.FullName);

            Assert.False(ClickOnceDestinationComparer.Instance.Equals(source.FullName, alias));
        }

        [Fact]
        public void Equals_WhenLeafIsExclusivelyLocked_StillDetectsCaseCollision()
        {
            using TestDirectory directory = new();
            FileInfo source = directory.CreateFile("payload.dll", "payload");
            using FileStream locked = File.Open(source.FullName, FileMode.Open, FileAccess.Read, FileShare.None);

            AssertEquivalent(source.FullName, Path.Combine(directory.FullPath, "PAYLOAD.DLL"));
        }

        [RequiresCaseSensitiveDirectoryFact]
        public void Equals_WhenSensitiveParentContainsMissingDescendants_PreservesInheritedSensitivity()
        {
            using TestDirectory directory = new();
            Assert.True(CaseSensitiveDirectory.TryEnable(new DirectoryInfo(directory.FullPath)));
            string first = Path.Combine(directory.FullPath, "Output", "payload.dll");
            string alias = Path.Combine(directory.FullPath, "Output", "PAYLOAD.DLL");

            Assert.False(ClickOnceDestinationComparer.Instance.Equals(first, alias));
            AssertEquivalent(first, @"\\?\" + first);
            Assert.False(Directory.Exists(Path.Combine(directory.FullPath, "Output")));
        }

        [Fact]
        public void Equals_WhenDirectoryDeniesListingButAllowsAttributes_PreservesDestinationComparison()
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Windows directory ACLs are required.");
            }
            using TestDirectory directory = new();
            DirectoryInfo restricted = Directory.CreateDirectory(Path.Combine(directory.FullPath, "Restricted"));
            DirectoryInfo child = restricted.CreateSubdirectory("Child");
            DirectorySecurity baseline = restricted.GetAccessControl();
            baseline.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
            restricted.SetAccessControl(baseline);
            string originalSddl = restricted.GetAccessControl()
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            DirectorySecurity changed = restricted.GetAccessControl();
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            changed.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory,
                InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
            try
            {
                restricted.SetAccessControl(changed);
                Assert.Throws<UnauthorizedAccessException>(() => restricted.EnumerateDirectories().ToArray());
                Assert.True((File.GetAttributes(restricted.FullName) & FileAttributes.Directory) != 0);
                AssertEquivalent(Path.Combine(child.FullName, "missing.dll"),
                    Path.Combine(child.FullName, "MISSING.DLL"));
            }
            finally
            {
                DirectorySecurity restore = new();
                restore.SetSecurityDescriptorSddlForm(originalSddl, AccessControlSections.Access);
                restricted.SetAccessControl(restore);
                Assert.Equal(originalSddl, restricted.GetAccessControl()
                    .GetSecurityDescriptorSddlForm(AccessControlSections.Access));
                Assert.Single(restricted.EnumerateDirectories());
            }
        }

        private static void AssertEquivalent(string first, string second)
        {
            Assert.True(ClickOnceDestinationComparer.Instance.Equals(first, second));
            Assert.True(ClickOnceDestinationComparer.Instance.Equals(second, first));
            Assert.Equal(ClickOnceDestinationComparer.Instance.GetHashCode(first),
                ClickOnceDestinationComparer.Instance.GetHashCode(second));
        }
    }
}
