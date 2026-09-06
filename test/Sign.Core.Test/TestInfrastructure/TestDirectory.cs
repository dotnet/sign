// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.IO;

namespace Sign.Core.Test
{
    internal sealed class TestDirectory : IDisposable
    {
        internal TestDirectory()
        {
            FullPath = Path.Combine(
                Path.GetTempPath(),
                Path.GetRandomFileName());
            Directory.CreateDirectory(FullPath);
        }

        internal string FullPath { get; }

        internal FileInfo CreateFile(
            string relativePath,
            string? contents = null)
        {
            string path = Path.Combine(FullPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path: path, contents: contents);

            return new FileInfo(path);
        }

        public void Dispose()
        {
            Directory.Delete(FullPath, recursive: true);
        }
    }
}
