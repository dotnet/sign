// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    internal sealed class DirectoryServiceStub : IDirectoryService
    {
        private readonly List<DirectoryInfo> _directories;

        internal IReadOnlyList<DirectoryInfo> Directories { get; }

        internal DirectoryServiceStub()
        {
            Directories = _directories = new List<DirectoryInfo>();
        }

        public DirectoryInfo CreateTemporaryDirectory()
        {
            DirectoryInfo directory = new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

            directory.Create();

            _directories.Add(directory);

            return directory;
        }

        public void Delete(DirectoryInfo directory)
        {
            directory.Refresh();

            if (directory.Exists)
            {
                directory.Delete(recursive: true);
            }
        }

        public void Dispose()
        {
            foreach (DirectoryInfo directory in _directories)
            {
                Delete(directory);
            }
        }
    }
}