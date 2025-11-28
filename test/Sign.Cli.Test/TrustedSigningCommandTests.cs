// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using Moq;
using Sign.Core;

namespace Sign.Cli.Test
{
    public class TrustedSigningCommandTests
    {
        private readonly TrustedSigningCommand _command = new(new CodeCommand(), Mock.Of<IServiceProviderFactory>());

        [Fact]
        public void Constructor_WhenCodeCommandIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningCommand(codeCommand: null!, Mock.Of<IServiceProviderFactory>()));

            Assert.Equal("codeCommand", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenServiceProviderFactoryIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TrustedSigningCommand(new CodeCommand(), serviceProviderFactory: null!));

            Assert.Equal("serviceProviderFactory", exception.ParamName);
        }

        [Fact]
        public void EndpointOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.EndpointOption.Arity);
        }

        [Fact]
        public void EndpointOption_Always_IsRequired()
        {
            Assert.True(_command.EndpointOption.Required);
        }

        [Fact]
        public void AccountOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.AccountOption.Arity);
        }

        [Fact]
        public void AccountOption_Always_IsRequired()
        {
            Assert.True(_command.AccountOption.Required);
        }

        [Fact]
        public void CertificateProfileOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.CertificateProfileOption.Arity);
        }

        [Fact]
        public void CertificateProfileOption_Always_IsRequired()
        {
            Assert.True(_command.CertificateProfileOption.Required);
        }

        public class ParserTests
        {
            private readonly TrustedSigningCommand _command;
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
            [InlineData("code trusted-signing")]
            [InlineData("code trusted-signing a")]
            [InlineData("code trusted-signing -tse")]
            [InlineData("code trusted-signing -tse https://trustedsigning.test")]
            [InlineData("code trusted-signing -tse https://trustedsigning.test -tsa")]
            [InlineData("code trusted-signing -tse https://trustedsigning.test -tsa a")]
            [InlineData("code trusted-signing -tse https://trustedsigning.test -tsa a -tscp b")]
            [InlineData("code trusted-signing -tse https://trustedsigning.test -tsa a -tscp b -kvt")]
            [InlineData("code trusted-signing -tse https://trustedsigning.test -tsa a -tscp b -kvt c")]
            [InlineData("code trusted-signing -tse https://trustedsigning.test -tsa a -tscp b -kvt c -kvi")]
            [InlineData("code trusted-signing -tse https://trustedsigning.test -tsa a -tscp b -kvt c -kvi d")]
            [InlineData("code trusted-signing -tse https://trustedsigning.test -tsa a -tscp b -kvt c -kvi d -kvs")]
            [InlineData("code trusted-signing -tse https://trustedsigning.test -tsa a -tscp b -kvt c -kvi d -kvs e")]
            public void Command_WhenRequiredArgumentOrOptionsAreMissing_HasError(string command)
            {
                ParseResult result = _rootCommand.Parse(command);

                Assert.NotEmpty(result.Errors);
            }

            [Theory]
            [InlineData("code trusted-signing -tse https://trustedsigning.test -tsa a -tscp b c")]
            [InlineData("code trusted-signing -tse https://trustedsigning.test -tsa a -tscp b -kvt c -kvi d -kvs e f")]
            public void Command_WhenRequiredArgumentsArePresent_HasNoError(string command)
            {
                ParseResult result = _rootCommand.Parse(command);

                Assert.Empty(result.Errors);
            }
        }
    }
}
