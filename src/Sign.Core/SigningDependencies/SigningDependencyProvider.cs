// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SigningDependencyProvider : ISigningDependencyProvider
    {
        private readonly IEnumerable<ISigningDependencyReader> _readers;

        // Dependency injection requires a public constructor.
        public SigningDependencyProvider(IEnumerable<ISigningDependencyReader> readers)
        {
            ArgumentNullException.ThrowIfNull(readers, nameof(readers));

            _readers = readers;
        }

        public IReadOnlyList<FileInfo> GetSigningDependencies(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            foreach (ISigningDependencyReader reader in _readers)
            {
                if (reader.CanRead(file))
                {
                    return reader.GetSigningDependencies(file);
                }
            }

            return Array.Empty<FileInfo>();
        }

        public IReadOnlyList<FileInfo> ExcludeOwnedFiles(IEnumerable<FileInfo> files)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));

            List<FileInfo> allFiles = files.ToList();
            HashSet<FileInfo> ownedFiles = new(FileInfoComparer.Instance);

            // Ownership is resolved against the original list so that a file owned by an owned file
            // is excluded too.
            foreach (FileInfo file in allFiles)
            {
                foreach (FileInfo dependency in GetSigningDependencies(file))
                {
                    ownedFiles.Add(dependency);
                }
            }

            if (ownedFiles.Count == 0)
            {
                return allFiles;
            }

            return allFiles.Where(file => !ownedFiles.Contains(file)).ToList();
        }
    }
}
