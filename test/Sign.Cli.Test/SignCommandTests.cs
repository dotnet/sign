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
            Assert.Equal(new[] { "--help", "-?", "-h", "/?", "/h" }, option.Aliases.Order());
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
            string command = "code azure-key-vault --name a --description b --description-url https://description.test "
                + "-kvu https://keyvault.test -kvc c -kvm";
            ParseResult result = _parser.Parse(command);

            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void Command_WhenRequiredArgumentsArePresent_HasNoError()
        {
            string command = "code azure-key-vault --name a --description b --description-url https://description.test "
                + "-kvu https://keyvault.test -kvc c -kvm d";
            ParseResult result = _parser.Parse(command);

            Assert.Empty(result.Errors);
        }
    }
}