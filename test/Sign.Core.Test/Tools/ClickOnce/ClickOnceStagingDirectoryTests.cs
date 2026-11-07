// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sign.Core.Test
{
    public sealed class ClickOnceStagingDirectoryTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Copy_PreservesReadOnlyUntilCleanup_EvenAfterRename(bool rename)
        {
            using DirectoryServiceStub directories = new();
            using TemporaryDirectory sourceDirectory = new(directories);
            FileInfo source = CreateReadOnlySource(sourceDirectory.Directory);
            ClickOnceStagingDirectory staging = new(directories);
            try
            {
                FileInfo copy = staging.Copy(source, new FileInfo(Path.Combine(staging.Directory.FullName, source.Name)));
                Assert.True(copy.IsReadOnly);
                if (rename)
                {
                    copy.MoveTo(Path.Combine(staging.Directory.FullName, "renamed.txt"));
                    Assert.True(copy.IsReadOnly);
                }

                staging.Dispose();

                staging.Directory.Refresh();
                Assert.False(staging.Directory.Exists);
                source.Refresh();
                Assert.True(source.IsReadOnly);
                Assert.Equal("source", System.IO.File.ReadAllText(source.FullName));
            }
            finally
            {
                source.IsReadOnly = false;
                staging.Dispose();
            }
        }

        [Fact]
        public void Dispose_WhenCopyFailsAfterReadOnlyCopy_DeletesCopiedStagingWithoutChangingSourceAttributes()
        {
            using DirectoryServiceStub directories = new();
            using TemporaryDirectory sourceDirectory = new(directories);
            FileInfo source = CreateReadOnlySource(sourceDirectory.Directory);
            ClickOnceStagingDirectory staging = new(directories);
            try
            {
                staging.Copy(source, new FileInfo(Path.Combine(staging.Directory.FullName, source.Name)));
                Assert.Throws<FileNotFoundException>(() => staging.Copy(
                    new FileInfo(Path.Combine(sourceDirectory.Directory.FullName, "missing.txt")),
                    new FileInfo(Path.Combine(staging.Directory.FullName, "missing.txt"))));

                staging.Dispose();

                staging.Directory.Refresh();
                Assert.False(staging.Directory.Exists);
                source.Refresh();
                Assert.True(source.IsReadOnly);
            }
            finally
            {
                source.IsReadOnly = false;
                staging.Dispose();
            }
        }

        [Fact]
        public void Dispose_WhenCopiedPathIsReplacedWithSourceHardLink_FailsWithoutClearingSourceReadOnly()
        {
            using DirectoryServiceStub directories = new();
            using TemporaryDirectory sourceDirectory = new(directories);
            FileInfo source = CreateReadOnlySource(sourceDirectory.Directory);
            ClickOnceStagingDirectory staging = new(directories);
            try
            {
                FileInfo copy = staging.Copy(source, new FileInfo(Path.Combine(staging.Directory.FullName, source.Name)));
                string originalDestination = copy.FullName;
                copy.MoveTo(Path.Combine(staging.Directory.FullName, "retained-copy.txt"));
                Link(originalDestination, source.FullName);

                Assert.Throws<UnauthorizedAccessException>(staging.Dispose);

                source.Refresh();
                Assert.True(source.IsReadOnly);
                Assert.True((System.IO.File.GetAttributes(originalDestination) & FileAttributes.ReadOnly) != 0);
                Assert.Equal("source", System.IO.File.ReadAllText(source.FullName));
            }
            finally
            {
                source.IsReadOnly = false;
                staging.Dispose();
            }
        }

        [Fact]
        public void Dispose_WhenStagedCopyHasExternalHardLink_RejectsCleanupWithoutClearingReadOnly()
        {
            using DirectoryServiceStub directories = new();
            using TemporaryDirectory sourceDirectory = new(directories);
            FileInfo source = CreateReadOnlySource(sourceDirectory.Directory);
            ClickOnceStagingDirectory staging = new(directories);
            FileInfo copy = staging.Copy(source, new FileInfo(Path.Combine(staging.Directory.FullName, source.Name)));
            FileInfo external = new(Path.Combine(sourceDirectory.Directory.FullName, "external.txt"));
            try
            {
                Link(external.FullName, copy.FullName);

                IOException failure = Assert.Throws<IOException>(staging.Dispose);

                Assert.Contains("aliased", failure.Message);
                copy.Refresh();
                external.Refresh();
                source.Refresh();
                Assert.True(copy.IsReadOnly);
                Assert.True(external.IsReadOnly);
                Assert.True(source.IsReadOnly);
            }
            finally
            {
                source.IsReadOnly = false;
                copy.IsReadOnly = false;
                staging.Dispose();
            }
        }

        private static FileInfo CreateReadOnlySource(DirectoryInfo directory)
        {
            FileInfo source = new(Path.Combine(directory.FullName, "source.txt"));
            System.IO.File.WriteAllText(source.FullName, "source");
            source.IsReadOnly = true;
            return source;
        }

        private static void Link(string path, string existing)
        {
            if (!CreateHardLink(path, existing, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLink(string path, string existing, IntPtr securityAttributes);
    }
}
