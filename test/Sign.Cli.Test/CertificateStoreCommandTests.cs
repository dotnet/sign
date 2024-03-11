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

        [Fact]
        public void SHA1_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.SHA1ThumbprintOption.Arity);
        }

        [Fact]
        public void Certificate_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.CertificatePathOption.Arity);
        }

        [Fact]
        public void CertificatePassword_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.CertificatePasswordOption.Arity);
        }

        [Fact]
        public void CryptoServiceProvider_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.CryptoServiceProvider.Arity);
        }

        [Fact]
        public void PrivateKeyContainer_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.PrivateKeyContainer.Arity);
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
            [InlineData("certificate-store a")]
            [InlineData("certificate-store a -s")]
            [InlineData("certificate-store a -s sha1 -cf")]
            [InlineData("certificate-store a -s sha1 -cf filePath -p")]
            [InlineData("certificate-store a -s sha1 -cf filePath -csp -k keyContainer")]
            [InlineData("certificate-store a -s sha1 -csp -k keyContainer")]
            [InlineData("certificate-store a -s sha1 -csp sampleCSP -k")]
            [InlineData("certificate-store a -s sha1 -csp sampleCSP -k -km")]
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