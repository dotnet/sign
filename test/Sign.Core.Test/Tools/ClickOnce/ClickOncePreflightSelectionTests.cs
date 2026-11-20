// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using NSubstitute;

namespace Sign.Core.Test
{
    public sealed class ClickOncePreflightSelectionTests
    {
        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(false, false, true, true)]
        [InlineData(true, false, false, false)]
        [InlineData(true, false, true, false)]
        [InlineData(false, true, false, false)]
        [InlineData(false, true, false, true)]
        [InlineData(true, true, false, false)]
        [InlineData(true, true, false, true)]
        [InlineData(true, true, true, false)]
        [InlineData(true, true, true, true)]
        public void IsSelected_WhenIncludeAndExcludeMatchersVary_PreservesPrecedenceAndOrdinalPathComparison(
            bool include, bool exclude, bool included, bool excluded)
        {
            IFileMatcher matcher = Substitute.For<IFileMatcher>();
            DirectoryInfo directory = new(Path.GetTempPath());
            FileInfo file = new(Path.Combine(directory.FullName, "Payload.dll"));
            Matcher inclusion = new();
            Matcher exclusion = new();
            matcher.EnumerateMatches(Arg.Any<DirectoryInfoBase>(), inclusion)
                .Returns(included ? new[] { new FileInfo(file.FullName) } : Array.Empty<FileInfo>());
            matcher.EnumerateMatches(Arg.Any<DirectoryInfoBase>(), exclusion)
                .Returns(excluded ? new[] { new FileInfo(file.FullName) } : Array.Empty<FileInfo>());
            ClickOncePreflightSelection selection = new(matcher,
                include ? inclusion : null, exclude ? exclusion : null);

            Assert.Equal((!include || included) && (!exclude || !excluded),
                selection.IsSelected(file, directory));
            Assert.Equal(!include, selection.IsSelected(
                new FileInfo(Path.Combine(directory.FullName, "payload.dll")), directory));
            Assert.Equal((!include || included) && (!exclude || !excluded),
                selection.IsSelected(new FileInfo(file.FullName), new DirectoryInfo(directory.FullName)));
            int expectedCalls = (include ? 1 : 0) + (exclude && (!include || included) ? 1 : 0);
            Assert.Equal(expectedCalls, matcher.ReceivedCalls().Count());
        }

        [Fact]
        public void IsSelected_WhenMatcherResultsChange_RetainsFirstSnapshotUntilDirectoryOrSelectionInstanceChanges()
        {
            IFileMatcher matcher = Substitute.For<IFileMatcher>();
            DirectoryInfo directory = new(Path.GetTempPath());
            DirectoryInfo other = new(Path.Combine(directory.FullName, "other"));
            FileInfo first = new(Path.Combine(directory.FullName, "first.dll"));
            FileInfo second = new(Path.Combine(directory.FullName, "second.dll"));
            List<FileInfo> matches = new() { first };
            Matcher pattern = new();
            matcher.EnumerateMatches(Arg.Any<DirectoryInfoBase>(), pattern).Returns(_ => matches);
            ClickOncePreflightSelection selection = new(matcher, pattern, null);

            Assert.True(selection.IsSelected(first, directory));
            matches.Clear();
            matches.Add(second);
            Assert.True(selection.IsSelected(first, directory));
            Assert.False(selection.IsSelected(second, directory));
            Assert.True(selection.IsSelected(second, other));
            Assert.True(new ClickOncePreflightSelection(matcher, pattern, null).IsSelected(second, directory));
            Assert.Equal(3, matcher.ReceivedCalls().Count());
        }

        [Fact]
        public void IsSelected_WhenIncludeAndExcludeSharePattern_EnumeratesOnceAndExcludesMatchingFile()
        {
            IFileMatcher matcher = Substitute.For<IFileMatcher>();
            DirectoryInfo directory = new(Path.GetTempPath());
            FileInfo file = new(Path.Combine(directory.FullName, "payload.dll"));
            Matcher pattern = new();
            matcher.EnumerateMatches(Arg.Any<DirectoryInfoBase>(), pattern).Returns(new[] { file });
            ClickOncePreflightSelection selection = new(matcher, pattern, pattern);

            Assert.False(selection.IsSelected(file, directory));
            Assert.False(selection.IsSelected(file, directory));
            Assert.Single(matcher.ReceivedCalls());
        }

        [Fact]
        public void IsSelected_WhenFirstEnumerationFails_PropagatesSameExceptionAndRetriesWithoutCachedFailure()
        {
            IFileMatcher matcher = Substitute.For<IFileMatcher>();
            DirectoryInfo directory = new(Path.GetTempPath());
            FileInfo file = new(Path.Combine(directory.FullName, "payload.dll"));
            Matcher pattern = new();
            IOException expected = new("Traversal failed");
            bool failed = false;
            matcher.EnumerateMatches(Arg.Any<DirectoryInfoBase>(), pattern).Returns(_ =>
            {
                if (!failed)
                {
                    failed = true;
                    throw expected;
                }
                return new[] { file };
            });
            ClickOncePreflightSelection selection = new(matcher, pattern, null);

            Assert.Same(expected, Assert.Throws<IOException>(() => selection.IsSelected(file, directory)));
            Assert.True(selection.IsSelected(file, directory));
            Assert.Equal(2, matcher.ReceivedCalls().Count());
        }
    }
}
