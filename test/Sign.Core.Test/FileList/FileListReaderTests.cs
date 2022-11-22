// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core.Test
{
    public class FileListReaderTests
    {
        private readonly FileListReader _reader;

        public FileListReaderTests()
        {
            _reader = new FileListReader(new MatcherSpyFactory());
        }

        [Fact]
        public void Read_WhenReaderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _reader.Read(reader: null!, out Matcher matcher, out Matcher antiMatcher));

            Assert.Equal("reader", exception.ParamName);
        }

        [Fact]
        public void Read_WhenFileListIsEmpty_ReturnsMatcher()
        {
            using (StreamReader streamReader = CreateFileList())
            {
                _reader.Read(streamReader, out Matcher matcher, out Matcher antiMatcher);

                var matcherSpy = (MatcherSpy)matcher;
                var antiMatcherSpy = (MatcherSpy)antiMatcher;

                Assert.Empty(matcherSpy.Includes);
                Assert.Empty(matcherSpy.Excludes);

                Assert.Empty(antiMatcherSpy.Includes);
                Assert.Empty(antiMatcherSpy.Excludes);
            }
        }

        [Theory]
        [InlineData("../*", "*")]
        [InlineData("a/../../*", "a/*")]
        [InlineData(@"..\*", "*")]
        [InlineData(@"a\..\..\*", "a/*")]
        public void Read_WhenFileListContainsParentDirectoryGlobs_RemovesParentDirectoryInPattern(string input, string expectedOutput)
        {
            using (StreamReader streamReader = CreateFileList(input))
            {
                _reader.Read(streamReader, out Matcher matcher, out Matcher antiMatcher);

                var matcherSpy = (MatcherSpy)matcher;
                var antiMatcherSpy = (MatcherSpy)antiMatcher;

                Assert.Equal(expectedOutput, Assert.Single(matcherSpy.Includes));
                Assert.Empty(matcherSpy.Excludes);

                Assert.Empty(antiMatcherSpy.Includes);
                Assert.Empty(antiMatcherSpy.Excludes);
            }
        }

        [Theory]
        [InlineData("!../*", "*")]
        [InlineData("!a/../../*", "a/*")]
        [InlineData(@"!..\*", "*")]
        [InlineData(@"!a\..\..\*", "a/*")]
        public void Read_WhenFileListContainsParentDirectoryAntiGlobs_RemovesParentDirectoryInPattern(string input, string expectedOutput)
        {
            using (StreamReader streamReader = CreateFileList(input))
            {
                _reader.Read(streamReader, out Matcher matcher, out Matcher antiMatcher);

                var matcherSpy = (MatcherSpy)matcher;
                var antiMatcherSpy = (MatcherSpy)antiMatcher;

                Assert.Empty(matcherSpy.Includes);
                Assert.Empty(matcherSpy.Excludes);

                Assert.Equal(expectedOutput, Assert.Single(antiMatcherSpy.Includes));
                Assert.Empty(antiMatcherSpy.Excludes);
            }
        }

        [Fact]
        public void Read_WhenFileListContainsIncludes_ReturnsMatcher()
        {
            string[] includes = new[] { "a/*", "b/*" };

            using (StreamReader streamReader = CreateFileList(includes))
            {
                _reader.Read(streamReader, out Matcher matcher, out Matcher antiMatcher);

                var matcherSpy = (MatcherSpy)matcher;
                var antiMatcherSpy = (MatcherSpy)antiMatcher;

                Assert.Equal(includes, matcherSpy.Includes);
                Assert.Empty(matcherSpy.Excludes);

                Assert.Empty(antiMatcherSpy.Includes);
                Assert.Empty(antiMatcherSpy.Excludes);
            }
        }

        [Fact]
        public void Read_WhenFileListContainsBothIncludeAndExclude_ReturnsMatcher()
        {
            const string include = "**/*";
            const string exclude = "!**/a.b";

            using (StreamReader streamReader = CreateFileList(include, exclude))
            {
                _reader.Read(streamReader, out Matcher matcher, out Matcher antiMatcher);

                var matcherSpy = (MatcherSpy)matcher;
                var antiMatcherSpy = (MatcherSpy)antiMatcher;

                Assert.Equal(include, Assert.Single(matcherSpy.Includes));
                Assert.Empty(matcherSpy.Excludes);

                Assert.Equal(exclude[1..], Assert.Single(antiMatcherSpy.Includes));
                Assert.Empty(antiMatcherSpy.Excludes);
            }
        }

        private static StreamReader CreateFileList(params string[] lines)
        {
            MemoryStream stream = new();

            using (StreamWriter writer = new(stream, leaveOpen: true))
            {
                foreach (string line in lines)
                {
                    writer.WriteLine(line);
                }
            }

            stream.Seek(offset: 0, SeekOrigin.Begin);

            return new StreamReader(stream);
        }

        private sealed class MatcherSpy : Matcher
        {
            private readonly List<string> _excludes = new();
            private readonly List<string> _includes = new();

            internal IReadOnlyList<string> Excludes
            {
                get => _excludes;
            }

            internal IReadOnlyList<string> Includes
            {
                get => _includes;
            }

            public override Matcher AddExclude(string pattern)
            {
                _excludes.Add(pattern);

                return base.AddExclude(pattern);
            }

            public override Matcher AddInclude(string pattern)
            {
                _includes.Add(pattern);

                return base.AddInclude(pattern);
            }
        }

        private sealed class MatcherSpyFactory : IMatcherFactory
        {
            public Matcher Create()
            {
                return new MatcherSpy();
            }
        }
    }
}