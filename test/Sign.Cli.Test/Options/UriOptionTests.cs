// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;

namespace Sign.Cli.Test
{
    public abstract class UriOptionTests : OptionTests<Uri>
    {
        private static readonly Uri ExpectedValue = new("https://domain.test");

        protected UriOptionTests(Option<Uri> option, string shortOption, string longOption, bool isRequired)
            : base(option, shortOption, longOption, ExpectedValue, isRequired)
        {
        }

        [Fact]
        public void Option_WhenValueFailsToParse_HasError()
        {
            VerifyHasError("3");
        }
    }
}