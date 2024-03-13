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
        public void Sha1ThumbprintOption_Always_IsRequired()
        {
            Assert.True(_command.Sha1ThumbprintOption.IsRequired);
        }

        [Fact]
        public void Sha1ThumbprintOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.Sha1ThumbprintOption.Arity);
        }

        [Fact]
        public void CertificateFileOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.CertificateFileOption.Arity);
        }

        [Fact]
        public void CertificatePasswordOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.CertificatePasswordOption.Arity);
        }

        [Fact]
        public void CryptoServiceProviderOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.CryptoServiceProviderOption.Arity);
        }

        [Fact]
        public void PrivateKeyContainerOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.PrivateKeyContainerOption.Arity);
        }

        public class ParserTests
        {
            private readonly CertificateStoreCommand _command;
            private readonly Parser _parser;

            public ParserTests()
            {
                _command = new CertificateStoreCommand(new CodeCommand(), Mock.Of<IServiceProviderFactory>());
                _parser = new CommandLineBuilder(_command).Build();
            }

            [Theory]
            [InlineData("certificate-store a")]
            [InlineData("certificate-store a -s")]
            [InlineData("certificate-store a -s sha1 -cf")]
            [InlineData("certificate-store a -s sha1 -cf filePath -p")]
            [InlineData("certificate-store a -s sha1 -cf filePath -csp ")]
            [InlineData("certificate-store a -s sha1 -cf filePath -csp sampleCSP -k")]
            [InlineData("certificate-store a -s sha1 -csp")]
            [InlineData("certificate-store a -s sha1 -csp sampleCSP -k")]
            public void Command_WhenRequiredArgumentOrOptionsAreMissing_HasError(string command)
            {
                ParseResult result = _parser.Parse(command);

                Assert.NotEmpty(result.Errors);
            }

            [Theory]
            [InlineData("certificate-store a -s sha1")]
            [InlineData("certificate-store a -s sha1 -cf filePath")]
            [InlineData("certificate-store a -s sha1 -cf filePath -p password")]
            [InlineData("certificate-store a -s sha1 -csp sampleCSP -k keyContainer ")]
            [InlineData("certificate-store a -s sha1 -csp sampleCSP -k machineKeyContainer -km")]
            [InlineData("certificate-store a -s sha1 -cf filePath -csp sampleCSP -k keyContainer ")]
            [InlineData("certificate-store a -s sha1 -cf filePath -p password -csp sampleCSP -k keyContainer")]
            [InlineData("certificate-store a -s sha1 -cf filePath -csp sampleCSP -k keyContainer -km")]
            public void Command_WhenRequiredArgumentsArePresent_HasNoError(string command)
            {
                ParseResult result = _parser.Parse(command);

                Assert.Empty(result.Errors);
            }
        }
    }
}