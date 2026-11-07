// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Sign.Core
{
    internal sealed class ClickOncePreflightSelection
    {
        private readonly IFileMatcher _fileMatcher;
        private readonly Matcher? _included;
        private readonly Matcher? _excluded;
        private readonly Dictionary<Matcher, Dictionary<string, HashSet<FileInfo>>> _matches =
            new(ReferenceEqualityComparer.Instance);

        internal ClickOncePreflightSelection(IFileMatcher fileMatcher, Matcher? included, Matcher? excluded)
        {
            _fileMatcher = fileMatcher;
            _included = included;
            _excluded = excluded;
        }

        internal bool IsSelected(FileInfo file, DirectoryInfo directory) =>
            (_included is null || Matches(directory, _included).Contains(file)) &&
            (_excluded is null || !Matches(directory, _excluded).Contains(file));

        private HashSet<FileInfo> Matches(DirectoryInfo directory, Matcher pattern)
        {
            if (!_matches.TryGetValue(pattern, out Dictionary<string, HashSet<FileInfo>>? directories))
            {
                directories = new(StringComparer.Ordinal);
                _matches.Add(pattern, directories);
            }
            if (!directories.TryGetValue(directory.FullName, out HashSet<FileInfo>? matches))
            {
                matches = _fileMatcher.EnumerateMatches(new DirectoryInfoWrapper(directory), pattern)
                    .ToHashSet(FileInfoComparer.Instance);
                directories.Add(directory.FullName, matches);
            }
            return matches;
        }
    }
}
