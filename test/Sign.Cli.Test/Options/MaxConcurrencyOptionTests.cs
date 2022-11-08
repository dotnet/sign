using System.CommandLine.Parsing;

namespace Sign.Cli.Test
{
    public class MaxConcurrencyOptionTests : Int32OptionTests
    {
        public MaxConcurrencyOptionTests()
            : base(new CodeCommand().MaxConcurrencyOption, "-m", "--max-concurrency", isRequired: false)
        {
        }

        [Fact]
        public void Option_WhenValueFailsToParse_HasError()
        {
            VerifyHasError("x");
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
            VerifyHasError($"{LongOption} {value}");
        }
    }
}