// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Parsing;

namespace Sign.Cli.Test
{
    public class SignCommandTests
    {
        private readonly Parser _parser;

        public SignCommandTests()
        {
            _parser = Program.CreateParser();
        }

        [Fact]
        public void Help_Always_IsEnabled()
        {
            ParseResult result = _parser.Parse("-?");
            Symbol symbol = result.CommandResult.Children.Single().Symbol;
            Option? option = symbol as Option;

            Assert.NotNull(option);

            string[] expectedAliases = new[] { "--help", "-?", "-h", "/?", "/h" };
            string[] actualAliases = option.Aliases.OrderBy(_ => _, StringComparer.Ordinal).ToArray();

            Assert.Equal(expectedAliases, actualAliases);
            Assert.Empty(result.Errors);
        }

        [Theory]
        [InlineData("code")]
        [InlineData("code azure-key-vault")]
        public void Command_WhenArgumentAndOptionsAreMissing_HasError(string command)
        {
            ParseResult result = _parser.Parse(command);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void Command_WhenRequiredArgumentIsMissing_HasError()
        {
            string command = "code azure-key-vault --description a --description-url https://description.test "
                + "-kvu https://keyvault.test -kvc b -kvm --timestamp-url http://timestamp.test";
            ParseResult result = _parser.Parse(command);

            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void Command_WhenRequiredArgumentIsPresent_HasNoError()
        {
            string command = "code azure-key-vault --description a --description-url https://description.test "
                + "-kvu https://keyvault.test -kvc b -kvm --timestamp-url http://timestamp.test c";
            ParseResult result = _parser.Parse(command);

            Assert.Empty(result.Errors);
        }
    }
}