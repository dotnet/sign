// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using Moq;
using Sign.Core;

namespace Sign.Cli.Test
{
    public class ArtifactSigningCommandTests
    {
        private readonly ArtifactSigningCommand _command = new(new CodeCommand(), Mock.Of<IServiceProviderFactory>());

        [Fact]
        public void Constructor_WhenCodeCommandIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ArtifactSigningCommand(codeCommand: null!, Mock.Of<IServiceProviderFactory>()));

            Assert.Equal("codeCommand", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenServiceProviderFactoryIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ArtifactSigningCommand(new CodeCommand(), serviceProviderFactory: null!));

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
            private readonly ArtifactSigningCommand _command;
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
            [InlineData("code artifact-signing")]
            [InlineData("code artifact-signing a")]
            [InlineData("code artifact-signing -ase")]
            [InlineData("code artifact-signing -ase https://artifactsigning.test")]
            [InlineData("code artifact-signing -ase https://artifactsigning.test -asa")]
            [InlineData("code artifact-signing -ase https://artifactsigning.test -asa a")]
            [InlineData("code artifact-signing -ase https://artifactsigning.test -asa a -ascp b")]
            [InlineData("code artifact-signing -ase https://artifactsigning.test -asa a -ascp b -kvt")]
            [InlineData("code artifact-signing -ase https://artifactsigning.test -asa a -ascp b -kvt c")]
            [InlineData("code artifact-signing -ase https://artifactsigning.test -asa a -ascp b -kvt c -kvi")]
            [InlineData("code artifact-signing -ase https://artifactsigning.test -asa a -ascp b -kvt c -kvi d")]
            [InlineData("code artifact-signing -ase https://artifactsigning.test -asa a -ascp b -kvt c -kvi d -kvs")]
            [InlineData("code artifact-signing -ase https://artifactsigning.test -asa a -ascp b -kvt c -kvi d -kvs e")]
            public void Command_WhenRequiredArgumentOrOptionsAreMissing_HasError(string command)
            {
                ParseResult result = _rootCommand.Parse(command);

                Assert.NotEmpty(result.Errors);
            }

            [Theory]
            [InlineData("code artifact-signing -ase https://artifactsigning.test -asa a -ascp b c")]
            [InlineData("code artifact-signing -ase https://artifactsigning.test -asa a -ascp b -kvt c -kvi d -kvs e f")]
            public void Command_WhenRequiredArgumentsArePresent_HasNoError(string command)
            {
                ParseResult result = _rootCommand.Parse(command);

                Assert.Empty(result.Errors);
            }
        }
    }
}
