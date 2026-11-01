// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Sign.Core
{
    internal abstract class Container : IContainer
    {
        private readonly IFileMatcher _fileMatcher;

        protected TemporaryDirectory? TemporaryDirectory { get; set; }

        protected Container(IFileMatcher fileMatcher)
        {
            ArgumentNullException.ThrowIfNull(fileMatcher, nameof(fileMatcher));

            _fileMatcher = fileMatcher;
        }

        public virtual void Dispose()
        {
            TemporaryDirectory?.Dispose();
        }

        public IEnumerable<SigningFile> GetFiles(
            SigningSourceIdentity parentIdentity)
        {
            ArgumentNullException.ThrowIfNull(
                parentIdentity,
                nameof(parentIdentity));

            if (TemporaryDirectory is null)
            {
                throw new InvalidOperationException();
            }

            return CreateSigningFiles(
                TemporaryDirectory.Directory.EnumerateFiles(
                    "*",
                    SearchOption.AllDirectories),
                parentIdentity);
        }

        public IEnumerable<SigningFile> GetFiles(
            Matcher matcher,
            SigningSourceIdentity parentIdentity)
        {
            ArgumentNullException.ThrowIfNull(matcher, nameof(matcher));
            ArgumentNullException.ThrowIfNull(
                parentIdentity,
                nameof(parentIdentity));

            if (TemporaryDirectory is null)
            {
                throw new InvalidOperationException();
            }

            DirectoryInfoWrapper directoryInfo = new(TemporaryDirectory.Directory);

            return CreateSigningFiles(
                _fileMatcher.EnumerateMatches(
                    directoryInfo,
                    matcher),
                parentIdentity);
        }

        public abstract ValueTask OpenAsync();
        public abstract ValueTask SaveAsync();

        private IEnumerable<SigningFile> CreateSigningFiles(
            IEnumerable<FileInfo> files,
            SigningSourceIdentity parentIdentity)
        {
            foreach (FileInfo file in files)
            {
                yield return SigningFile.ContainerEntry(
                    file,
                    parentIdentity,
                    Path.GetRelativePath(
                        TemporaryDirectory!.Directory.FullName,
                        file.FullName));
            }
        }
    }
}