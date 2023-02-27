// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine.Parsing;

namespace Sign.Cli.Test
{
    public class TimestampUrlOptionTests : UriOptionTests
    {
        public TimestampUrlOptionTests()
            : base(new CodeCommand().TimestampUrlOption, "-t", "--timestamp-url")
        {
        }

        [Fact]
        public void Option_WhenOptionIsMissing_HasDefaultValue()
        {
            ParseResult result = Parse();
            Uri? value = result.GetValueForOption(Option);

            Assert.NotNull(value);
            Assert.Equal("http://timestamp.acs.microsoft.com/", value.AbsoluteUri);
        }
    }
}