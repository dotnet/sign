// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.IO.Compression;
using Sign.Core;

namespace Sign.TestInfrastructure
{
    public static class TestFileCreator
    {
        internal static FileInfo CreateEmptyZipFile(TemporaryDirectory temporaryDirectory, string fileExtension)
        {
            FileInfo file = new(Path.Combine(temporaryDirectory.Directory.FullName, $"{Path.GetRandomFileName()}{fileExtension}"));

            using (FileStream stream = file.OpenWrite())
            using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
            {
            }

            return file;
        }
    }
}
