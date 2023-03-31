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

        private readonly Parser _parser;
        private readonly CodeCommand _codeCommand;
        private readonly AzureKeyVaultCommand _azureKeyVaultCommand;

        public SignCommandTests()
        {
            _parser = Program.CreateParser();

            SignCommand? signCommand = _parser.Configuration.RootCommand as SignCommand;

            Assert.NotNull(signCommand);

            CodeCommand? codeCommand = signCommand.Children
                .Where(child => child is CodeCommand)
                .Single() as CodeCommand;

            Assert.NotNull(codeCommand);

            AzureKeyVaultCommand? azureKeyVaultCommand = codeCommand.Children
                .Where(child => child is AzureKeyVaultCommand)
                .Single() as AzureKeyVaultCommand;

            Assert.NotNull(azureKeyVaultCommand);

            _codeCommand = codeCommand;
            _azureKeyVaultCommand = azureKeyVaultCommand;
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
            string command = $"code azure-key-vault --description {Description} --description-url {DescriptionUrl} "
                + $"-kvu {KeyVaultUrl} -kvc {CertificateName} -kvm --timestamp-url {TimestampUrl}";
            ParseResult result = _parser.Parse(command);

            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void Command_WhenAllOptionsAndArgumentAreValid_HasNoError()
        {
            string command = $"code azure-key-vault --description {Description} --description-url {DescriptionUrl} "
                + $"-kvu {KeyVaultUrl} -kvc {CertificateName} -kvm --timestamp-url {TimestampUrl} {File}";
            ParseResult result = _parser.Parse(command);

            Assert.Empty(result.Errors);

            Assert.Equal(Description, result.GetValueForOption(_codeCommand.DescriptionOption));
            Assert.Equal(DescriptionUrl, result.GetValueForOption(_codeCommand.DescriptionUrlOption)!.OriginalString);
            Assert.Equal(KeyVaultUrl, result.GetValueForOption(_azureKeyVaultCommand.UrlOption)!.OriginalString);
            Assert.Equal(CertificateName, result.GetValueForOption(_azureKeyVaultCommand.CertificateOption));
            Assert.Equal(TimestampUrl, result.GetValueForOption(_codeCommand.TimestampUrlOption)!.OriginalString);
            Assert.Equal(File, result.GetValueForArgument(_azureKeyVaultCommand.FileArgument));
        }
    }
}