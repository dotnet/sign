// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
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
            Assert.True(_command.CertificateOption.IsRequired);
        }

        [Fact]
        public void ClientIdOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.ClientIdOption.Arity);
        }

        [Fact]
        public void ClientIdOption_Always_IsNotRequired()
        {
            Assert.False(_command.ClientIdOption.IsRequired);
        }

        [Fact]
        public void ClientSecretOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.ClientSecretOption.Arity);
        }

        [Fact]
        public void ClientSecretOption_Always_IsNotRequired()
        {
            Assert.False(_command.ClientSecretOption.IsRequired);
        }

        [Fact]
        public void ManagedIdentityOption_Always_HasArityOfZeroOrOne()
        {
            Assert.Equal(ArgumentArity.ZeroOrOne, _command.ManagedIdentityOption.Arity);
        }

        [Fact]
        public void ManagedIdentityOption_Always_IsNotRequired()
        {
            Assert.False(_command.ManagedIdentityOption.IsRequired);
        }

        [Fact]
        public void TenantIdOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.TenantIdOption.Arity);
        }

        [Fact]
        public void TenantIdOption_Always_IsNotRequired()
        {
            Assert.False(_command.TenantIdOption.IsRequired);
        }

        [Fact]
        public void UrlOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.UrlOption.Arity);
        }

        [Fact]
        public void UrlOption_Always_IsRequired()
        {
            Assert.True(_command.UrlOption.IsRequired);
        }

        public class ParserTests
        {
            private readonly AzureKeyVaultCommand _command;
            private readonly Parser _parser;

            public ParserTests()
            {
                _command = new(new CodeCommand(), Mock.Of<IServiceProviderFactory>());
                _parser = new CommandLineBuilder(_command).Build();
            }

            [Theory]
            [InlineData("azure-key-vault")]
            [InlineData("azure-key-vault a")]
            [InlineData("azure-key-vault -kvu")]
            [InlineData("azure-key-vault -kvu https://keyvault.test")]
            [InlineData("azure-key-vault -kvu https://keyvault.test a")]
            [InlineData("azure-key-vault -kvu https://keyvault.test -kvc")]
            [InlineData("azure-key-vault -kvu https://keyvault.test -kvc a")]
            [InlineData("azure-key-vault -kvu https://keyvault.test -kvc a -kvt")]
            [InlineData("azure-key-vault -kvu https://keyvault.test -kvc a -kvt b")]
            [InlineData("azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi")]
            [InlineData("azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi c")]
            [InlineData("azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi c -kvs")]
            [InlineData("azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi c -kvs d")]
            public void Command_WhenRequiredArgumentOrOptionsAreMissing_HasError(string command)
            {
                ParseResult result = _parser.Parse(command);

                Assert.NotEmpty(result.Errors);
            }

            [Theory]
            [InlineData("azure-key-vault -kvu https://keyvault.test -kvc a -kvm b")]
            [InlineData("azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi c -kvs d e")]
            public void Command_WhenRequiredArgumentsArePresent_HasNoError(string command)
            {
                ParseResult result = _parser.Parse(command);

                Assert.Empty(result.Errors);
            }
        }
    }
}