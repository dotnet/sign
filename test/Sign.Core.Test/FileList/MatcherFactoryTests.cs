// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core.Test
{
    public class MatcherFactoryTests
    {
        private static readonly MatcherFactory _matcherFactory = new();

        [Fact]
        public void StringComparison_Always_IsCaseInsensitive()
        {
            Assert.Equal(StringComparison.OrdinalIgnoreCase, _matcherFactory.StringComparison);
        }

        [Theory]
        [InlineData("file.ZIP")] // Turkish I (U+0049)
        [InlineData("file.zip")] // Turkish i (U+0069)
        [InlineData("file.zİp")] // Turkish İ (U+0130)
        [InlineData("file.zıp")] // Turkish ı (U+0131)
        public void Create_Always_CreatesCaseInsensitiveMatcher(string fileName)
        {
            bool expectedResult = string.Equals(".zip", Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase);
            string directoryPath = Path.GetTempPath();
            string filePath = Path.Combine(directoryPath, fileName);
            InMemoryDirectoryInfo directoryInfo = new(directoryPath, new[] { filePath });
            Matcher matcher = _matcherFactory.Create();

            matcher.AddInclude("**/*.zip");

            PatternMatchingResult result = matcher.Execute(directoryInfo);

            Assert.Equal(expectedResult, result.HasMatches);

            if (result.HasMatches)
            {
                FilePatternMatch match = Assert.Single(result.Files);

                Assert.Equal(fileName, match.Path);
            }
        }
    }
}