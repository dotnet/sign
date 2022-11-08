using System.CommandLine;
using System.CommandLine.Parsing;

namespace Sign.Cli.Test
{
    public abstract class OptionTests<T>
    {
        private readonly T? _expectedValue;
        private readonly bool _isRequired;

        protected Option<T> Option { get; }
        protected string LongOption { get; }
        protected string ShortOption { get; }

        protected OptionTests(Option<T> option, string shortOption, string longOption, T? expectedValue, bool isRequired)
        {
            Option = option;
            ShortOption = shortOption;
            LongOption = longOption;
            _expectedValue = expectedValue;
            _isRequired = isRequired;
        }

        [Fact]
        public void Option_WhenOptionIsMissing_HasParseErrorsOnlyIfRequired()
        {
            VerifyIsRequired();
        }

        [Fact]
        public void Option_WithOnlyValue_HasParseErrors()
        {
            VerifyHasError("x");
        }

        [Fact]
        public void Option_WithShortOptionAndMissingValue_HasParseErrors()
        {
            VerifyHasError(ShortOption);
        }

        [Fact]
        public void Option_WithLongOptionAndMissingValue_HasParseErrors()
        {
            VerifyHasError(LongOption);
        }

        [Fact]
        public void Option_WithShortOptionThenValue_ParsesValueOnlyIfShortOptionHasSingleCharacterAlias()
        {
            // From https://learn.microsoft.com/en-us/dotnet/standard/commandline/syntax#option-argument-delimiters
            // "A POSIX convention lets you omit the delimiter when you are specifying a single-character option alias."

            string commandLine = $"{ShortOption}{_expectedValue}";

            if (ShortOption.Length == 2)
            {
                Verify(commandLine);
            }
            else
            {
                VerifyHasError(commandLine);
            }
        }

        [Fact]
        public void Option_WithShortOptionSpaceThenValue_ParsesValue()
        {
            Verify($"{ShortOption} {_expectedValue}");
        }

        [Fact]
        public void Option_WithLongOptionSpaceThenValue_ParsesValue()
        {
            Verify($"{LongOption} {_expectedValue}");
        }

        protected void Verify(string commandLine)
        {
            Verify(commandLine, _expectedValue);
        }

        protected void Verify(string commandLine, T? expectedValue)
        {
            ParseResult result = Parse(commandLine);

            Assert.Empty(result.Errors);

            T? actualValue = result.GetValueForOption(Option);

            VerifyEqual(expectedValue, actualValue);
        }

        protected virtual void VerifyEqual(T? expectedValue, T? actualValue)
        {
            Assert.Equal(expectedValue, actualValue);
        }

        protected void VerifyHasError(string commandLine)
        {
            ParseResult result = Parse(commandLine);

            Assert.NotEmpty(result.Errors);
        }

        private void VerifyIsRequired()
        {
            ParseResult result = Parse();

            if (_isRequired)
            {
                Assert.NotEmpty(result.Errors);
            }
            else
            {
                Assert.Empty(result.Errors);
            }
        }

        protected ParseResult Parse(string commandLine = "")
        {
            RootCommand rootCommand = new() { Option };

            return rootCommand.Parse(commandLine);
        }
    }
}