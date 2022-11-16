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

        public IEnumerable<FileInfo> GetFiles()
        {
            if (TemporaryDirectory is null)
            {
                throw new InvalidOperationException();
            }

            return TemporaryDirectory.Directory.EnumerateFiles("*", SearchOption.AllDirectories);
        }

        public IEnumerable<FileInfo> GetFiles(Matcher matcher)
        {
            ArgumentNullException.ThrowIfNull(matcher, nameof(matcher));

            if (TemporaryDirectory is null)
            {
                throw new InvalidOperationException();
            }

            DirectoryInfoWrapper directoryInfo = new(TemporaryDirectory.Directory);

            return _fileMatcher.EnumerateMatches(directoryInfo, matcher);
        }

        public abstract ValueTask OpenAsync();
        public abstract ValueTask SaveAsync();
    }
}