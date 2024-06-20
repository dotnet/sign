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
            Assert.True(_command.EndpointOption.IsRequired);
        }

        [Fact]
        public void AccountOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.AccountOption.Arity);
        }

        [Fact]
        public void AccountOption_Always_IsRequired()
        {
            Assert.True(_command.AccountOption.IsRequired);
        }

        [Fact]
        public void CertificateProfileOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.CertificateProfileOption.Arity);
        }

        [Fact]
        public void CertificateProfileOption_Always_IsRequired()
        {
            Assert.True(_command.CertificateProfileOption.IsRequired);
        }

        public class ParserTests
        {
            private readonly TrustedSigningCommand _command;
            private readonly Parser _parser;

            public ParserTests()
            {
                _command = new(new CodeCommand(), Mock.Of<IServiceProviderFactory>());
                _parser = new CommandLineBuilder(_command).Build();
            }

            [Theory]
            [InlineData("trusted-signing")]
            [InlineData("trusted-signing a")]
            [InlineData("trusted-signing -tse")]
            [InlineData("trusted-signing -tse https://trustedsigning.test")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tscp b")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tscp b -kvt")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tscp b -kvt c")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tscp b -kvt c -kvi")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tscp b -kvt c -kvi d")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tscp b -kvt c -kvi d -kvs")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tscp b -kvt c -kvi d -kvs e")]
            public void Command_WhenRequiredArgumentOrOptionsAreMissing_HasError(string command)
            {
                ParseResult result = _parser.Parse(command);

                Assert.NotEmpty(result.Errors);
            }

            [Theory]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tscp b c")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tscp b -kvt c -kvi d -kvs e f")]
            public void Command_WhenRequiredArgumentsArePresent_HasNoError(string command)
            {
                ParseResult result = _parser.Parse(command);

                Assert.Empty(result.Errors);
            }
        }
    }
}
