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

        private const string Sha1Fingerprint = "da39a3ee5e6b4b0d3255bfef95601890afd80709";
        private const string Sha256Fingerprint = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        private const string Sha384Fingerprint = "38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da274edebfe76f65fbd51ad2f14898b95b";
        private const string Sha512Fingerprint = "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e";

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
        public void CertificateFingerprintOption_Always_IsRequired()
        {
            Assert.True(_command.CertificateFingerprintOption.IsRequired);
        }

        [Fact]
        public void CertificateFingerprintOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.CertificateFingerprintOption.Arity);
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
            [InlineData("certificate-store a -cfp")]
            [InlineData("certificate-store a -cfp -cf")]
            [InlineData($"certificate-store a -cfp {Sha256Fingerprint} -cf")]
            [InlineData($"certificate-store a -cfp {Sha256Fingerprint} -cf filePath -p")]
            [InlineData($"certificate-store a -cfp {Sha256Fingerprint} -cf filePath -csp")]
            [InlineData($"certificate-store a -cfp {Sha256Fingerprint} -cf filePath -csp sampleCSP -k")]
            [InlineData($"certificate-store a -cfp {Sha256Fingerprint} -csp")]
            [InlineData($"certificate-store a -cfp {Sha256Fingerprint} -csp sampleCSP -k")]
            public void Command_WhenRequiredArgumentOrOptionsAreMissing_HasError(string command)
            {
                ParseResult result = _parser.Parse(command);

                Assert.NotEmpty(result.Errors);
            }

            [Theory]
            [InlineData("certificate-store a -cfp \"\"")]
            [InlineData("certificate-store a -cfp b")]
            [InlineData("certificate-store a -cfp b c")]
            [InlineData($"certificate-store a -cfp {Sha1Fingerprint}")]
            [InlineData("certificate-store a -cfp Z3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")] // SHA-256 length, but contains a non-hex character
            public void Command_WhenCertificateFingerprintAlgorithmCannotBeDeduced_HasError(string command)
            {
                ParseResult result = _parser.Parse(command);

                Assert.NotEmpty(result.Errors);
            }

            [Theory]
            [InlineData($"certificate-store a -cfp {Sha256Fingerprint}")]
            [InlineData($"certificate-store a -cfp {Sha384Fingerprint} -cf filePath")]
            [InlineData($"certificate-store a -cfp {Sha512Fingerprint} -cf filePath -p password")]
            [InlineData($"certificate-store a -cfp {Sha256Fingerprint} -csp sampleCSP -k keyContainer ")]
            [InlineData($"certificate-store a -cfp {Sha256Fingerprint} -csp sampleCSP -k machineKeyContainer -km")]
            [InlineData($"certificate-store a -cfp {Sha256Fingerprint} -cf filePath -csp sampleCSP -k keyContainer ")]
            [InlineData($"certificate-store a -cfp {Sha256Fingerprint} -cf filePath -p password -csp sampleCSP -k keyContainer")]
            [InlineData($"certificate-store a -cfp {Sha256Fingerprint} -cf filePath -csp sampleCSP -k keyContainer -km")]
            public void Command_WhenRequiredArgumentsArePresent_HasNoError(string command)
            {
                ParseResult result = _parser.Parse(command);

                Assert.Empty(result.Errors);
            }
        }
    }
}
