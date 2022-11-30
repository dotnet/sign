// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    internal sealed class FileMetadataServiceStub : IFileMetadataService
    {
        internal List<FileInfo> PortableExecutableFiles { get; } = new();

        public bool IsPortableExecutable(FileInfo file)
        {
            return PortableExecutableFiles.Contains(file, FileInfoComparer.Instance);
        }
    }
}