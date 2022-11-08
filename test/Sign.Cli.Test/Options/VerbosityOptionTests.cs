using Microsoft.Extensions.Logging;

namespace Sign.Cli.Test
{
    public class VerbosityOptionTests : OptionTests<LogLevel>
    {
        private const LogLevel ExpectedValue = LogLevel.Debug;

        public VerbosityOptionTests()
            : base(new CodeCommand().VerbosityOption, "-v", "--verbosity", ExpectedValue, isRequired: false)
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