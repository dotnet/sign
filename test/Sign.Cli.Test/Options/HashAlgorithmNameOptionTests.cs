using System.CommandLine;
using System.CommandLine.Parsing;
using System.Security.Cryptography;

namespace Sign.Cli.Test
{
    public abstract class HashAlgorithmNameOptionTests : OptionTests<HashAlgorithmName>
    {
        private static readonly HashAlgorithmName ExpectedValue = HashAlgorithmName.SHA256;

        protected HashAlgorithmNameOptionTests(Option<HashAlgorithmName> option, string shortOption, string longOption, bool isRequired)
            : base(option, shortOption, longOption, ExpectedValue, isRequired)
        {
        }

        [Theory]
        [InlineData("sha256")]
        [InlineData("SHA256")]
        public void Option_WhenValueIsSha256_ParsesValue(string value)
        {
            Verify($"{LongOption} {value}", HashAlgorithmName.SHA256);
        }

        [Theory]
        [InlineData("sha384")]
        [InlineData("SHA384")]
        public void Option_WhenValueIsSha384_ParsesValue(string value)
        {
            Verify($"{LongOption} {value}", HashAlgorithmName.SHA384);
        }

        [Theory]
        [InlineData("sha512")]
        [InlineData("SHA512")]
        public void Option_WhenValueIsSha512_ParsesValue(string value)
        {
            Verify($"{LongOption} {value}", HashAlgorithmName.SHA512);
        }

        [Theory]
        [InlineData("md5")]
        [InlineData("sha1")]
        [InlineData("sha-256")]
        public void Verbosity_WhenValueIsInvalid_HasError(string value)
        {
            VerifyHasError($"{LongOption} {value}");
        }

        [Fact]
        public void Option_WhenOptionIsMissing_HasDefaultValue()
        {
            ParseResult result = Parse();
            HashAlgorithmName value = result.GetValueForOption(Option);

            Assert.Equal(HashAlgorithmName.SHA256, value);
        }
    }
}