// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine.Parsing;

namespace Sign.Cli.Test
{
    public class MaxConcurrencyOptionTests : Int32OptionTests
    {
        public MaxConcurrencyOptionTests()
            : base(new CodeCommand().MaxConcurrencyOption, "-m", "--max-concurrency")
        {
        }

        [Fact]
        public void Option_WhenValueFailsToParse_HasError()
        {
            const string value = "x";

            VerifyHasErrors(value, GetUnrecognizedCommandOrArgumentMessage(value));
        }

        [Fact]
        public void Option_WhenOptionIsMissing_HasDefaultValue()
        {
            ParseResult result = Parse();
            int value = result.GetValueForOption(Option);

            Assert.Equal(4, value);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Option_WhenValueIsLessThanOne_HasError(int value)
        {
            VerifyHasErrors(
                $"{LongOption} {value}",
                GetFormattedResourceString(Resources.InvalidMaxConcurrencyValue, LongOption));
        }
    }
}