// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal static class ClickOncePathSpelling
    {
        internal static bool IsSameDirectory(DirectoryInfo sourceRoot, DirectoryInfo destinationRoot)
        {
            if (string.Equals(sourceRoot.FullName, destinationRoot.FullName, StringComparison.Ordinal))
            {
                return true;
            }
            if (!string.Equals(sourceRoot.FullName, destinationRoot.FullName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            try
            {
                _ = File.GetAttributes(sourceRoot.FullName);
                _ = File.GetAttributes(destinationRoot.FullName);
            }
            catch (Exception exception) when (
                exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return false;
            }

            DirectoryInfo? anchor = sourceRoot.Parent;
            if (anchor is null)
            {
                return true;
            }
            while (anchor.Parent is not null &&
                !destinationRoot.FullName.StartsWith(
                    PathContainment.GetFullDirectoryPath(anchor.FullName), StringComparison.Ordinal))
            {
                anchor = anchor.Parent;
            }
            return string.Equals(Bind(sourceRoot, anchor).FullName,
                Bind(destinationRoot, anchor).FullName, StringComparison.Ordinal);
        }

        internal static FileSystemInfo Bind(FileSystemInfo source, DirectoryInfo sourceRoot)
        {
            string[] segments = Path.GetRelativePath(sourceRoot.FullName, source.FullName)
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            DirectoryInfo directory = sourceRoot;
            for (int index = 0; index < segments.Length; ++index)
            {
                bool last = index == segments.Length - 1;
                FileSystemInfo[] candidates = (last && source is FileInfo
                    ? directory.EnumerateFiles().Cast<FileSystemInfo>()
                    : directory.EnumerateDirectories().Cast<FileSystemInfo>())
                    .Where(entry => string.Equals(entry.Name, segments[index], StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                FileSystemInfo? exact = candidates.SingleOrDefault(entry =>
                    string.Equals(entry.Name, segments[index], StringComparison.Ordinal));
                FileSystemInfo entry = exact ?? (candidates.Length == 1
                    ? candidates[0]
                    : throw new IOException($"Original payload source '{source.FullName}' has no unambiguous filesystem spelling."));
                if (last)
                {
                    return entry;
                }
                directory = (DirectoryInfo)entry;
            }

            throw new IOException($"Original path '{source.FullName}' does not identify a filesystem entry.");
        }
    }
}
