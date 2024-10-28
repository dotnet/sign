// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.TestInfrastructure
{
    public sealed class TemporaryFile : IDisposable
    {
        public FileInfo File { get; }

        public TemporaryFile()
        {
            File = new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        }

        public void Dispose()
        {
            File.Refresh();

            if (File.Exists)
            {
                File.Delete();

                File.Refresh();
            }
        }
    }
}
