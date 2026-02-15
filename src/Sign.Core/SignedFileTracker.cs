// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;

namespace Sign.Core
{
    internal sealed class SignedFileTracker : ISignedFileTracker
    {
        // ConcurrentDictionary is used as a thread-safe set (no built-in ConcurrentHashSet).
        // Byte is used as a minimal placeholder value.
        private readonly ConcurrentDictionary<string, byte> _signedFiles;

        // Dependency injection requires a public constructor.
        public SignedFileTracker()
        {
            _signedFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        }

        public bool HasSigned(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            string canonicalPath = GetCanonicalPath(file);

            return _signedFiles.ContainsKey(canonicalPath);
        }

        public void MarkAsSigned(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            string canonicalPath = GetCanonicalPath(file);

            _ = _signedFiles.TryAdd(canonicalPath, value: 0);
        }

        private static string GetCanonicalPath(FileInfo file)
        {
            // Use Path.GetFullPath to resolve relative paths, . and .. segments, and normalize separators
            return Path.GetFullPath(file.FullName);
        }
    }
}
