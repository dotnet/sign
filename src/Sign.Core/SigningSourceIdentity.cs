// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Win32.SafeHandles;

namespace Sign.Core
{
    internal sealed class SigningSourceIdentity :
        IEquatable<SigningSourceIdentity>
    {
        private readonly string _path;
        private readonly SigningSourceIdentity? _parent;

        private SigningSourceIdentity(
            string path,
            SigningSourceIdentity? parent)
        {
            _path = path;
            _parent = parent;
        }

        private bool IsContainerEntry => _parent is not null;

        internal static SigningSourceIdentity Capture(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return PhysicalFile(file.FullName);
        }

        internal static SigningSourceIdentity PhysicalFile(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));

            return new SigningSourceIdentity(
                path: GetCanonicalPhysicalPath(path),
                parent: null);
        }

        internal static SigningSourceIdentity ContainerEntry(
            SigningSourceIdentity parent,
            string entryPath)
        {
            ArgumentNullException.ThrowIfNull(parent, nameof(parent));

            return new SigningSourceIdentity(
                path: NormalizeEntryPath(entryPath),
                parent: parent);
        }

        internal SigningSourceIdentity GetSibling(string relativePath)
        {
            string normalizedRelativePath =
                NormalizeEntryPath(relativePath);

            return IsContainerEntry
                ? ContainerEntry(
                    _parent!,
                    CombineEntryPaths(
                        Path.GetDirectoryName(_path.Replace(
                            '/',
                            Path.DirectorySeparatorChar)),
                        normalizedRelativePath))
                : PhysicalFile(
                    Path.Combine(
                        Path.GetDirectoryName(_path)!,
                        normalizedRelativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));
        }

        // A container entry belongs to the physical file that contains it.
        internal SigningSourceIdentity GetPhysicalRoot()
        {
            return _parent?.GetPhysicalRoot() ?? this;
        }

        internal string GetPhysicalPath()
        {
            return GetPhysicalRoot()._path;
        }

        // Compares path text, as Equals does, so every identity that
        // GetSibling derives from the file is in its directory tree.
        internal bool IsInDirectoryTreeOf(SigningSourceIdentity file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            string? directory = Path.GetDirectoryName(
                file.GetPhysicalRoot()._path);

            return directory is not null &&
                PathContainment.IsContained(
                    PathContainment.EnsureTrailingSeparator(directory),
                    GetPhysicalRoot()._path);
        }

        public bool Equals(SigningSourceIdentity? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (
                other is null ||
                IsContainerEntry != other.IsContainerEntry)
            {
                return false;
            }

            if (!IsContainerEntry)
            {
                return string.Equals(
                    a: _path,
                    b: other._path,
                    comparisonType: StringComparison.Ordinal);
            }

            return
                _parent!.Equals(other._parent) &&
                string.Equals(
                    a: _path,
                    b: other._path,
                    comparisonType: StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as SigningSourceIdentity);
        }

        public override int GetHashCode()
        {
            return IsContainerEntry
                ? HashCode.Combine(
                    _parent,
                    StringComparer.Ordinal.GetHashCode(_path))
                : StringComparer.Ordinal.GetHashCode(_path);
        }

        private static string GetCanonicalPhysicalPath(string path)
        {
            string fullPath = RemoveExtendedPathPrefix(
                Path.GetFullPath(path));

            try
            {
                using SafeFileHandle handle = File.OpenHandle(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                return ResolvePathSpelling(fullPath);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                NotSupportedException)
            {
                // If the file cannot be opened, preserve its exact path
                // spelling rather than assuming a case-only alias.
                return fullPath;
            }
        }

        private static string RemoveExtendedPathPrefix(string path)
        {
            const string extendedPrefix = @"\\?\";
            const string extendedUncPrefix = @"\\?\UNC\";

            if (path.StartsWith(
                extendedUncPrefix,
                StringComparison.Ordinal))
            {
                return $@"\\{path[extendedUncPrefix.Length..]}";
            }

            return path.StartsWith(
                extendedPrefix,
                StringComparison.Ordinal)
                    ? path[extendedPrefix.Length..]
                    : path;
        }

        private static string ResolvePathSpelling(string fullPath)
        {
            string? root = Path.GetPathRoot(fullPath);

            if (root is null)
            {
                return fullPath;
            }

            string resolvedPath = root;
            string[] segments = fullPath[root.Length..].Split(
                new[]
                {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                },
                StringSplitOptions.RemoveEmptyEntries);
            EnumerationOptions options = new()
            {
                AttributesToSkip = 0,
                IgnoreInaccessible = false,
                MatchCasing = MatchCasing.PlatformDefault,
                MatchType = MatchType.Simple,
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false
            };

            foreach (string segment in segments)
            {
                FileSystemInfo? match = FindEntry(
                    new DirectoryInfo(resolvedPath),
                    segment,
                    options);

                if (match is null)
                {
                    return fullPath;
                }

                resolvedPath = Path.Combine(
                    resolvedPath,
                    match.Name);
            }

            return resolvedPath;
        }

        private static FileSystemInfo? FindEntry(
            DirectoryInfo directory,
            string name,
            EnumerationOptions options)
        {
            FileSystemInfo? caseInsensitiveMatch = null;

            foreach (
                FileSystemInfo entry
                in directory.EnumerateFileSystemInfos(name, options))
            {
                if (string.Equals(
                    entry.Name,
                    name,
                    StringComparison.Ordinal))
                {
                    return entry;
                }

                if (!string.Equals(
                    entry.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (caseInsensitiveMatch is not null)
                {
                    return null;
                }

                caseInsensitiveMatch = entry;
            }

            return caseInsensitiveMatch;
        }

        private static string NormalizeEntryPath(string entryPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                entryPath,
                nameof(entryPath));

            if (Path.IsPathRooted(entryPath))
            {
                throw new ArgumentException(
                    message: "The entry path must be relative.",
                    paramName: nameof(entryPath));
            }

            List<string> segments = new();

            foreach (
                string segment
                in entryPath.Replace('\\', '/').Split(
                    separator: '/',
                    options: StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    if (segments.Count == 0)
                    {
                        throw new ArgumentException(
                            message:
                                "The entry path cannot escape the " +
                                "container root.",
                            paramName: nameof(entryPath));
                    }

                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }

                segments.Add(segment);
            }

            if (segments.Count == 0)
            {
                throw new ArgumentException(
                    message: "The normalized entry path cannot be empty.",
                    paramName: nameof(entryPath));
            }

            return string.Join(separator: '/', values: segments);
        }

        private static string CombineEntryPaths(
            string? directory,
            string relativePath)
        {
            return string.IsNullOrEmpty(directory)
                ? relativePath
                : $"{directory.Replace('\\', '/')}/{relativePath}";
        }
    }
}
