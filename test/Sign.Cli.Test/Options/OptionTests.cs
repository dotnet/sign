// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

namespace Sign.Cli.Test
{
    public abstract class OptionTests<T>
    {
        private readonly T? _expectedValue;

        protected Option<T> Option { get; }
        protected string LongOption { get; }
        protected string ShortOption { get; }

        protected OptionTests(Option<T> option, string shortOption, string longOption, T? expectedValue)
        {
            Option = option;
            ShortOption = shortOption;
            LongOption = longOption;
            _expectedValue = expectedValue;
        }

        [Fact]
        public void Option_WhenOptionIsMissing_HasParseErrorsOnlyIfRequired()
        {
            VerifyIsRequired();
        }

        [Fact]
        public void Option_WithOnlyValue_HasParseErrors()
        {
            const string value = "x";

            if (Option.IsRequired)
            {
                VerifyHasErrors(
                    value,
                    GetOptionIsRequiredMessage(ShortOption),
                    GetUnrecognizedCommandOrArgumentMessage(value));
            }
            else
            {
                VerifyHasErrors(value, GetUnrecognizedCommandOrArgumentMessage(value));
            }
        }

        [Fact]
        public void Option_WithShortOptionAndMissingValue_HasParseErrors()
        {
            VerifyHasErrors(ShortOption, GetRequiredArgumentMissingMessage(ShortOption));
        }

        [Fact]
        public void Option_WithLongOptionAndMissingValue_HasParseErrors()
        {
            VerifyHasErrors(LongOption, GetRequiredArgumentMissingMessage(LongOption));
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
                VerifyHasErrors(commandLine, GetUnrecognizedCommandOrArgumentMessage(commandLine));
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

        protected void VerifyHasErrors(string commandLine, params string[] expectedErrorMessages)
        {
            ParseResult result = Parse(commandLine);
            HashSet<string> expectedMessages = new(expectedErrorMessages, StringComparer.Ordinal);
            HashSet<string> actualMessages = result.Errors
                .Select(error => error.Message)
                .ToHashSet(StringComparer.Ordinal);

            Assert.NotEmpty(actualMessages);
            Assert.Equal(expectedMessages, actualMessages);
        }

        private void VerifyIsRequired()
        {
            ParseResult result = Parse();

            if (Option.IsRequired)
            {
                ParseError parseError = Assert.Single(result.Errors);
                string actualMessage = parseError.Message;
                string expectedMessage = GetOptionIsRequiredMessage(ShortOption);

                Assert.Equal(expectedMessage, actualMessage);
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

        protected static string GetFormattedResourceString(string resourceString, params string[] arguments)
        {
            return string.Format(CultureInfo.CurrentCulture, resourceString, arguments);
        }

        private static string GetRequiredArgumentMissingMessage(string argumentName)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                "Required argument missing for option: '{0}'.",
                argumentName);
        }

        protected static string GetOptionIsRequiredMessage(string optionName)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                "Option '{0}' is required.",
                optionName);
        }

        protected static string GetUnrecognizedCommandOrArgumentMessage(string name)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                "Unrecognized command or argument '{0}'.",
                name);
        }
    }
}