// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class DirectoryServiceStubTests
    {
        [Fact]
        public void CreateTemporaryDirectory_InvokesCallbackOnEachEmptyDirectoryBeforeReturning()
        {
            List<DirectoryInfo> created = new();
            using DirectoryServiceStub directories = new(directory =>
            {
                Assert.True(directory.Exists);
                Assert.Empty(directory.EnumerateFileSystemInfos());
                created.Add(directory);
                File.WriteAllText(Path.Combine(directory.FullName, "configured.txt"), "configured");
            });

            DirectoryInfo first = directories.CreateTemporaryDirectory();
            DirectoryInfo second = directories.CreateTemporaryDirectory();

            Assert.Equal(new[] { first, second }, created);
            Assert.Equal(new[] { first, second }, directories.Directories);
            Assert.Equal("configured", File.ReadAllText(Path.Combine(first.FullName, "configured.txt")));
            Assert.Equal("configured", File.ReadAllText(Path.Combine(second.FullName, "configured.txt")));
        }

        [Fact]
        public void CreateTemporaryDirectory_WhenCallbackThrows_DeletesDirectoryBeforePropagating()
        {
            InvalidOperationException expected = new("Directory configuration failed.");
            DirectoryInfo? created = null;
            using DirectoryServiceStub directories = new(directory =>
            {
                created = directory;
                File.WriteAllText(Path.Combine(directory.FullName, "configured.txt"), "configured");
                throw expected;
            });

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(
                () => directories.CreateTemporaryDirectory());

            Assert.Same(expected, actual);
            Assert.NotNull(created);
            created.Refresh();
            Assert.False(created.Exists);
        }
    }
}
