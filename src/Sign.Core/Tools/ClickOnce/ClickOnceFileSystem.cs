// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal static class ClickOnceFileSystem
    {
        internal static bool IsFile(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            try
            {
                return !File.GetAttributes(file.FullName).HasFlag(FileAttributes.Directory);
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
        }
    }
}
