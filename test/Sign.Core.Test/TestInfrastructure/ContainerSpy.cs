// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core.Test
{
    internal sealed class ContainerSpy : IContainer
    {
        private readonly FileMatcher _fileMatcher = new();

        internal ushort Dispose_CallCount { get; private set; }
        internal FileInfo File { get; }
        internal HashSet<FileInfo> Files { get; } = new(FileInfoComparer.Instance);
        internal ushort GetFiles_CallCount { get; private set; }
        internal ushort GetFilesWithMatcher_CallCount { get; private set; }
        internal ushort OpenAsync_CallCount { get; private set; }
        internal ushort SaveAsync_CallCount { get; private set; }

        internal ContainerSpy(FileInfo file)
        {
            File = file;
        }

        public void Dispose()
        {
            ++Dispose_CallCount;
        }

        public IEnumerable<FileInfo> GetFiles()
        {
            ++GetFiles_CallCount;

            return Files;
        }

        public IEnumerable<FileInfo> GetFiles(Matcher matcher)
        {
            ++GetFilesWithMatcher_CallCount;

            List<string> inMemoryFiles = Files.Select(file => file.FullName).ToList();
            InMemoryDirectoryInfo directoryInfo = new(File.DirectoryName!, inMemoryFiles);

            return _fileMatcher.EnumerateMatches(directoryInfo, matcher);
        }

        public ValueTask OpenAsync()
        {
            ++OpenAsync_CallCount;

            return ValueTask.CompletedTask;
        }

        public ValueTask SaveAsync()
        {
            ++SaveAsync_CallCount;

            return ValueTask.CompletedTask;
        }
    }
}