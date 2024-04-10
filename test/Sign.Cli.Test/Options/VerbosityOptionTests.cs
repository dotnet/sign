// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;

namespace Sign.Cli.Test
{
    public class VerbosityOptionTests : OptionTests<LogLevel>
    {
        private const LogLevel ExpectedValue = LogLevel.Debug;

        public VerbosityOptionTests()
            : base(new CodeCommand().VerbosityOption, "-v", "--verbosity", ExpectedValue)
        {
        }

        [Theory]
        [InlineData(LogLevel.Trace)]
        [InlineData(LogLevel.Debug)]
        [InlineData(LogLevel.Information)]
        [InlineData(LogLevel.Warning)]
        [InlineData(LogLevel.Error)]
        [InlineData(LogLevel.Critical)]
        [InlineData(LogLevel.None)]
        public void Verbosity_WhenValueIsValid_ParsesValue(LogLevel logLevel)
        {
            Verify($"{LongOption} {logLevel}", logLevel);
        }

        [Fact]
        public void Verbosity_WhenValueCasingDoesNotMatchEnumMemberCasing_ParsesValue()
        {
            LogLevel logLevel = LogLevel.Warning;

            Verify($"{LongOption} {logLevel.ToString().ToUpperInvariant()}", logLevel);
        }
    }
}