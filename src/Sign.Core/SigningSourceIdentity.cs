// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SigningSourceIdentity :
        IEquatable<SigningSourceIdentity>
    {
        private readonly IdentityKind _kind;
        private readonly string? _path;
        private readonly SigningSourceIdentity? _parent;

        private SigningSourceIdentity(
            IdentityKind kind,
            string? path,
            SigningSourceIdentity? parent)
        {
            _kind = kind;
            _path = path;
            _parent = parent;
        }

        internal static SigningSourceIdentity Capture(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return PhysicalFile(file.FullName);
        }

        internal static SigningSourceIdentity PhysicalFile(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));

            return new SigningSourceIdentity(
                kind: IdentityKind.PhysicalPath,
                path: Path.GetFullPath(path),
                parent: null);
        }

        internal static SigningSourceIdentity ContainerEntry(
            SigningSourceIdentity parent,
            string entryPath)
        {
            ArgumentNullException.ThrowIfNull(parent, nameof(parent));

            return new SigningSourceIdentity(
                kind: IdentityKind.ContainerEntry,
                path: NormalizeEntryPath(entryPath),
                parent: parent);
        }

        public bool Equals(SigningSourceIdentity? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null || _kind != other._kind)
            {
                return false;
            }

            return _kind switch
            {
                IdentityKind.PhysicalPath => string.Equals(
                    a: _path,
                    b: other._path,
                    comparisonType: StringComparison.OrdinalIgnoreCase),
                IdentityKind.ContainerEntry =>
                    _parent!.Equals(other._parent) &&
                    string.Equals(
                        a: _path,
                        b: other._path,
                        comparisonType: StringComparison.Ordinal),
                _ => false
            };
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as SigningSourceIdentity);
        }

        public override int GetHashCode()
        {
            return _kind switch
            {
                IdentityKind.PhysicalPath => HashCode.Combine(
                    _kind,
                    StringComparer.OrdinalIgnoreCase.GetHashCode(_path!)),
                IdentityKind.ContainerEntry => HashCode.Combine(
                    _kind,
                    _parent,
                    StringComparer.Ordinal.GetHashCode(_path!)),
                _ => 0
            };
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

        private enum IdentityKind
        {
            PhysicalPath,
            ContainerEntry
        }
    }
}
