// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Parsing;

namespace Sign.Cli.Test
{
    public partial class SignCommandTests
    {
        private const string Description = "a";
        private const string DescriptionUrl = "https://description.test";
        private const string KeyVaultUrl = "https://keyvault.test";
        private const string CertificateName = "b";
        private const string TimestampUrl = "http://timestamp.test";
        private const string File = "c";

        private readonly SignCommand _signCommand;
        private readonly CodeCommand _codeCommand;
        private readonly AzureKeyVaultCommand _azureKeyVaultCommand;

        public SignCommandTests()
        {
            _signCommand = Program.CreateCommand();

            Assert.NotNull(_signCommand);

            CodeCommand? codeCommand = _signCommand.Subcommands
                .Where(child => child is CodeCommand)
                .Single() as CodeCommand;

            Assert.NotNull(codeCommand);

            AzureKeyVaultCommand? azureKeyVaultCommand = codeCommand.Subcommands
                .Where(child => child is AzureKeyVaultCommand)
                .Single() as AzureKeyVaultCommand;

            Assert.NotNull(azureKeyVaultCommand);

            _codeCommand = codeCommand;
            _azureKeyVaultCommand = azureKeyVaultCommand;
        }

        [Fact]
        public void Help_Always_IsEnabled()
        {
            ParseResult result = _signCommand.Parse("-?");
            SymbolResult symbolResult = result.CommandResult.Children.Single();
            OptionResult? optionResult = symbolResult as OptionResult;

            Assert.NotNull(optionResult);

            string[] expectedAliases = new[] { "-?", "-h", "/?", "/h" };
            string[] actualAliases = optionResult.Option.Aliases.OrderBy(_ => _, StringComparer.Ordinal).ToArray();

            Assert.Equal(expectedAliases, actualAliases);
            Assert.Empty(result.Errors);
        }

        [Theory]
        [InlineData("code")]
        [InlineData("code azure-key-vault")]
        public void Command_WhenArgumentAndOptionsAreMissing_HasError(string command)
        {
            ParseResult result = _signCommand.Parse(command);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void Command_WhenRequiredArgumentIsMissing_HasError()
        {
            string command = $"code --description {Description} --description-url {DescriptionUrl} --timestamp-url {TimestampUrl} "
                + $"azure-key-vault -kvu {KeyVaultUrl} -kvc {CertificateName}";
            ParseResult result = _signCommand.Parse(command);

            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void Command_WhenAllOptionsAndArgumentAreValid_HasNoError()
        {
            string command = $"code --description {Description} --description-url {DescriptionUrl} --timestamp-url {TimestampUrl} "
                + $"azure-key-vault -kvu {KeyVaultUrl} -kvc {CertificateName} {File}";
            ParseResult result = _signCommand.Parse(command);

            Assert.Empty(result.Errors);

            Assert.Equal(Description, result.GetValue(_codeCommand.DescriptionOption));
            Assert.Equal(DescriptionUrl, result.GetValue(_codeCommand.DescriptionUrlOption)!.OriginalString);
            Assert.Equal(KeyVaultUrl, result.GetValue(_azureKeyVaultCommand.UrlOption)!.OriginalString);
            Assert.Equal(CertificateName, result.GetValue(_azureKeyVaultCommand.CertificateOption));
            Assert.Equal(TimestampUrl, result.GetValue(_codeCommand.TimestampUrlOption)!.OriginalString);
            Assert.Equal([File], result.GetValue(_azureKeyVaultCommand.FilesArgument));
        }
    }
}
