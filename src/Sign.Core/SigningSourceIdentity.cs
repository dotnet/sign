// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

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
                path: Path.GetFullPath(path),
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
                    comparisonType: StringComparison.OrdinalIgnoreCase);
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
                : StringComparer.OrdinalIgnoreCase.GetHashCode(_path);
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
