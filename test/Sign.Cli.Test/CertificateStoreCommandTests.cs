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
    public class CertificateStoreCommandTests
    {
        private readonly CertificateStoreCommand _command = new(new CodeCommand(), Mock.Of<IServiceProviderFactory>());

        [Fact]
        public void Constructor_WhenCodeCommandIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new CertificateStoreCommand(codeCommand: null!, Mock.Of<IServiceProviderFactory>()));

            Assert.Equal("codeCommand", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenServiceProviderFactoryIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new CertificateStoreCommand(new CodeCommand(), serviceProviderFactory: null!));

            Assert.Equal("serviceProviderFactory", exception.ParamName);
        }

        [Fact]
        public void SHA1ThumbprintOptionOption_Always_IsRequired()
        {
            Assert.True(_command.SHA1ThumbprintOption.IsRequired);
        }

        public class ParserTests
        {
            private readonly CertificateStoreCommand _command;
            private readonly Parser _parser;

            public ParserTests()
            {
                _command = new(new CodeCommand(), Mock.Of<IServiceProviderFactory>());
                _parser = new CommandLineBuilder(_command).Build();
            }

            [Theory]
            [InlineData("certificate-store")]
            [InlineData("certificate-store a")]
            [InlineData("certificate-store -u")]
            [InlineData("certificate-store -u https://keyvault.test -d \"testCert\"")]
            [InlineData("certificate-store -s TestingSha -d \"testCert\"")]
            [InlineData("certificate-store -s TestingSha -f \"testCert.pfx\" -d \"testCert\"")]
            [InlineData("certificate-store -s TestingSha -csp \"MyStore\" -k \"MyContainer\" -d \"testCert\" -u \"testDesc\"")]
            public void Command_WhenRequiredArgumentOrOptionsAreMissing_HasError(string command)
            {
                ParseResult result = _parser.Parse(command);

                Assert.NotEmpty(result.Errors);
            }

            [Theory]
            [InlineData("certificate-store -s -u -d")]
            [InlineData("certificate-store -s -f -d")]
            [InlineData("certificate-store -s -f -p -u -d")]
            [InlineData("certificate-store -s -csp -k -u -d")]
            [InlineData("certificate-store -s -csp -km -u -d")]
            [InlineData("certificate-store -s -f -csp -k -u")]
            [InlineData("certificate-store -s -f -p -csp -k -u -d")]
            [InlineData("certificate-store -s -f -csp -km -u")]
            public void Command_WhenRequiredArgumentsArePresent_HasNoError(string command)
            {
                ParseResult result = _parser.Parse(command);

                Assert.Empty(result.Errors);
            }
        }
    }
}