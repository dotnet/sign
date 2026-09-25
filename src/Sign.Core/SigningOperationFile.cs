// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal static class SigningOperationFile
    {
        internal static Exception? TryDelete(FileInfo file)
        {
            try
            {
                file.Delete();

                return null;
            }
            catch (Exception exception) when (
                exception is FileNotFoundException or
                DirectoryNotFoundException)
            {
                return null;
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                return exception;
            }
        }
    }
}
