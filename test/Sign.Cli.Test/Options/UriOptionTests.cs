// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;

namespace Sign.Cli.Test
{
    public abstract class UriOptionTests : OptionTests<Uri?>
    {
        private static readonly Uri ExpectedValue = new("https://domain.test");

        protected UriOptionTests(Option<Uri?> option, string shortOption, string longOption, bool isRequired)
            : base(option, shortOption, longOption, ExpectedValue, isRequired)
        {
        }

        [Fact]
        public void Option_WhenValueFailsToParse_HasError()
        {
            VerifyHasError("3");
        }

        [Theory]
        [InlineData("http://domain.test")]
        [InlineData("https://domain.test")]
        public void Option_WithShortOptionAndValidUrl_ParsesValue(string url)
        {
            Uri expectedValue = new(url, UriKind.Absolute);

            Verify($"{ShortOption} {expectedValue.OriginalString}", expectedValue);
        }

        [Theory]
        [InlineData("//domain.test")]
        [InlineData("/path")]
        [InlineData("file:///file.bin")]
        public void Option_WithShortOptionAndInvalidUrl_HasErrors(string invalidUrl)
        {
            VerifyHasError($"{ShortOption} {invalidUrl}");
        }

        protected override void VerifyEqual(Uri? expectedValue, Uri? actualValue)
        {
            Assert.NotNull(expectedValue);
            Assert.NotNull(actualValue);
            Assert.Equal(expectedValue.IsAbsoluteUri, actualValue.IsAbsoluteUri);
            Assert.Equal(expectedValue.AbsoluteUri, actualValue.AbsoluteUri);
        }
    }
}