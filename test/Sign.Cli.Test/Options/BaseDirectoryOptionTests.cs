// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine.Parsing;
using System.Globalization;

namespace Sign.Cli.Test
{
    public class BaseDirectoryOptionTests : DirectoryInfoOptionTests
    {
        public BaseDirectoryOptionTests()
            : base(new CodeCommand().BaseDirectoryOption, "-b", "--base-directory")
        {
        }

        [Fact]
        public void Option_WhenOptionIsMissing_HasDefaultValue()
        {
            ParseResult result = Parse();
            DirectoryInfo? value = result.GetValueForOption(Option);

            VerifyEqual(new DirectoryInfo(Environment.CurrentDirectory), value);
        }

        [Theory]
        [InlineData("directory")]
        [InlineData(@".\directory")]
        public void Option_WhenValueIsNotRooted_HasError(string relativePath)
        {
            VerifyHasErrors(
                $"{LongOption} {relativePath}",
                string.Format(CultureInfo.CurrentCulture, Resources.InvalidBaseDirectoryValue, "--base-directory"));
        }

        [Fact]
        public void Option_WhenValueIsRooted_ParsesValue()
        {
            DirectoryInfo directory = new(".");

            Verify($"{LongOption} \"{directory.FullName}\"", directory);
        }
    }
}