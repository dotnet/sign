// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Sign.Core.Test
{
    public class FileMatcherTests
    {
        private static readonly bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        private static readonly StringComparison StringComparison = StringComparison.OrdinalIgnoreCase;
        private static readonly MatcherFactory MatcherFactory = new();

        private readonly FileMatcher _fileMatcher;
        private readonly Matcher _matcher;
        private readonly string[] _files;
        private readonly DirectoryInfoBase _directory;

        public FileMatcherTests()
        {
            _fileMatcher = new FileMatcher();
            _matcher = MatcherFactory.Create();

            string rootDirectory = IsWindows ? @"C:\work" : "/work";

            _files = new[]
            {
                $"{rootDirectory}{Path.DirectorySeparatorChar}a",
                $"{rootDirectory}{Path.DirectorySeparatorChar}.b",
                $"{rootDirectory}{Path.DirectorySeparatorChar}c.d",
                $"{rootDirectory}{Path.DirectorySeparatorChar}e",
                $"{rootDirectory}{Path.DirectorySeparatorChar}E",
                $"{rootDirectory}{Path.DirectorySeparatorChar}f{Path.DirectorySeparatorChar}a",
                $"{rootDirectory}{Path.DirectorySeparatorChar}f{Path.DirectorySeparatorChar}.b",
                $"{rootDirectory}{Path.DirectorySeparatorChar}f{Path.DirectorySeparatorChar}c.d",
                $"{rootDirectory}{Path.DirectorySeparatorChar}f{Path.DirectorySeparatorChar}e",
                $"{rootDirectory}{Path.DirectorySeparatorChar}f{Path.DirectorySeparatorChar}E"
            };
            _directory = new InMemoryDirectoryInfo(rootDirectory, _files);
        }

        [Fact]
        public void EnumerateMatches_WhenDirectoryIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _fileMatcher.EnumerateMatches(directory: null!, _matcher));

            Assert.Equal("directory", exception.ParamName);
        }

        [Fact]
        public void EnumerateMatches_WhenMatcherIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _fileMatcher.EnumerateMatches(_directory, matcher: null!));

            Assert.Equal("matcher", exception.ParamName);
        }

        [Fact]
        public void EnumerateMatches_WhenMatcherHasInclusion_IncludesFiles()
        {
            _matcher.AddInclude("**/.b");

            IEnumerable<FileInfo> results = _fileMatcher.EnumerateMatches(_directory, _matcher);
            string[] actual = results.Select(file => file.FullName).ToArray();

            Assert.Equal(_files.Where(file => file.EndsWith(".b", StringComparison)), actual);
        }

        [Fact]
        public void EnumerateMatches_WhenMatcherHasOnlyExclusion_ReturnsEmptyResults()
        {
            _matcher.AddExclude("**/c.d");

            IEnumerable<FileInfo> results = _fileMatcher.EnumerateMatches(_directory, _matcher);

            // Exclusions with no inclusions yields no results.
            Assert.Empty(results);
        }

        [Fact]
        public void EnumerateMatches_WhenMatcherHasBothInclusionAndExclusion_IncludesAndExcludesFiles()
        {
            _matcher.AddInclude("**/f/*");
            _matcher.AddExclude("**/.b");

            IEnumerable<FileInfo> results = _fileMatcher.EnumerateMatches(_directory, _matcher);

            string[] expected = _files.Where(file => file.Contains('f') && !file.EndsWith(".b", StringComparison)).ToArray();
            string[] actual = results.Select(file => file.FullName).ToArray();

            Assert.Equal(_files.Where(file => file.Contains('f') && !file.EndsWith(".b", StringComparison)), actual);
        }

        [Fact]
        public void EnumerateMatches_WhenFilesDifferOnlyInCasing_AppliesMatchCaseSensitively()
        {
            _matcher.AddInclude("**/E");

            IEnumerable<FileInfo> results = _fileMatcher.EnumerateMatches(_directory, _matcher);
            string[] actual = results.Select(file => file.FullName).ToArray();

            Assert.Equal(_files.Where(file => file.EndsWith("E", StringComparison)), actual);
        }
    }
}