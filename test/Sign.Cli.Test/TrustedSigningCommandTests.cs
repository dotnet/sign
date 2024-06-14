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
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tsc b")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tsc b -tst")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tsc b -tst c")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tsc b -tst c -tsi")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tsc b -tst c -tsi d")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tsc b -tst c -tsi d -tss")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tsc b -tst c -tsi d -tss e")]
            public void Command_WhenRequiredArgumentOrOptionsAreMissing_HasError(string command)
            {
                ParseResult result = _parser.Parse(command);

                Assert.NotEmpty(result.Errors);
            }

            [Theory]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tsc b -tsm c")]
            [InlineData("trusted-signing -tse https://trustedsigning.test -tsa a -tsc b -tst c -tsi d -tss e f")]
            public void Command_WhenRequiredArgumentsArePresent_HasNoError(string command)
            {
                ParseResult result = _parser.Parse(command);

                Assert.Empty(result.Errors);
            }
        }
    }
}
