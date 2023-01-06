// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class FileMetadataService : IFileMetadataService
    {
        public bool IsPortableExecutable(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            using (FileStream stream = file.OpenRead())
            {
                var buffer = new byte[2];
                if (stream.CanRead)
                {
                    int read = stream.Read(buffer, offset: 0, count: 2);
                    if (read == 2)
                    {
                        // Look for the magic MZ header 
                        return buffer[0] == 0x4d && buffer[1] == 0x5a;
                    }
                }
            }

            return false;
        }
    }
}