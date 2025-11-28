// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using Moq;
using Sign.Core;

namespace Sign.Cli.Test
{
    public class AzureKeyVaultCommandTests
    {
        private readonly AzureKeyVaultCommand _command = new(new CodeCommand(), Mock.Of<IServiceProviderFactory>());

        [Fact]
        public void Constructor_WhenCodeCommandIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AzureKeyVaultCommand(codeCommand: null!, Mock.Of<IServiceProviderFactory>()));

            Assert.Equal("codeCommand", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenServiceProviderFactoryIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AzureKeyVaultCommand(new CodeCommand(), serviceProviderFactory: null!));

            Assert.Equal("serviceProviderFactory", exception.ParamName);
        }

        [Fact]
        public void CertificateOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.CertificateOption.Arity);
        }

        [Fact]
        public void CertificateOption_Always_IsRequired()
        {
            Assert.True(_command.CertificateOption.Required);
        }

        [Fact]
        public void UrlOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.UrlOption.Arity);
        }

        [Fact]
        public void UrlOption_Always_IsRequired()
        {
            Assert.True(_command.UrlOption.Required);
        }

        public class ParserTests
        {
            private readonly AzureKeyVaultCommand _command;
            private readonly RootCommand _rootCommand;

            public ParserTests()
            {
                CodeCommand codeCommand = new();
                _command = new(codeCommand, Mock.Of<IServiceProviderFactory>());
                _rootCommand = new RootCommand();
                _rootCommand.Subcommands.Add(codeCommand);
                codeCommand.Subcommands.Add(_command);
            }

            [Theory]
            [InlineData("code azure-key-vault")]
            [InlineData("code azure-key-vault a")]
            [InlineData("code azure-key-vault -kvu")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test a")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt b")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi c")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi c -kvs")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi c -kvs d")]
            public void Command_WhenRequiredArgumentOrOptionsAreMissing_HasError(string command)
            {
                ParseResult result = _rootCommand.Parse(command);

                Assert.NotEmpty(result.Errors);
            }

            [Theory]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a b")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi c -kvs d e")]
            public void Command_WhenRequiredArgumentsArePresent_HasNoError(string command)
            {
                ParseResult result = _rootCommand.Parse(command);

                Assert.Empty(result.Errors);
            }

            [Theory]
            [InlineData("code azure-key-vault -kvu \"\" -kvc a b")]
            [InlineData("code azure-key-vault -kvu //keyvault.test -kvc a b")]
            [InlineData("code azure-key-vault -kvu /path -kvc a b")]
            [InlineData("code azure-key-vault -kvu file:///file.bin -kvc a b")]
            [InlineData("code azure-key-vault -kvu http://keyvault.test -kvc a b")]
            [InlineData("code azure-key-vault -kvu ftp://keyvault.test -kvc a b")]
            public void Command_WhenUrlIsInvalid_HasError(string command)
            {
                ParseResult result = _rootCommand.Parse(command);

                Assert.NotEmpty(result.Errors);
                Assert.Contains(result.Errors, error => error.Message.Contains("URL"));
            }

            [Theory]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a b", "https://keyvault.test/")]
            [InlineData("code azure-key-vault -kvu https://my-vault.vault.azure.test -kvc cert b", "https://my-vault.vault.azure.test/")]
            [InlineData("code azure-key-vault -kvu HTTPS://KEYVAULT.TEST -kvc a b", "https://keyvault.test/")]
            public void Command_WhenUrlIsValidHttps_ParsesCorrectly(string command, string expectedUrl)
            {
                ParseResult result = _rootCommand.Parse(command);

                Assert.Empty(result.Errors);
                Uri? actualUrl = result.GetValue(_command.UrlOption);
                Assert.NotNull(actualUrl);
                Assert.Equal(expectedUrl, actualUrl.AbsoluteUri);
            }
        }
    }
}
