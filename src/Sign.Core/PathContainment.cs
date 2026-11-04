// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal static class PathContainment
    {
        internal static string EnsureTrailingSeparator(string path)
        {
            return Path.EndsInDirectorySeparator(path)
                ? path
                : $"{path}{Path.DirectorySeparatorChar}";
        }

        internal static string GetFullDirectoryPath(string directoryPath)
        {
            return EnsureTrailingSeparator(Path.GetFullPath(directoryPath));
        }

        internal static bool IsContained(
            string rootPathWithTrailingSeparator,
            string fullPath)
        {
            return fullPath.StartsWith(
                rootPathWithTrailingSeparator,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
