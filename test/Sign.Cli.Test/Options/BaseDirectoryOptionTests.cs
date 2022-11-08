using System.CommandLine.Parsing;

namespace Sign.Cli.Test
{
    public class BaseDirectoryOptionTests : DirectoryInfoOptionTests
    {
        public BaseDirectoryOptionTests()
            : base(new CodeCommand().BaseDirectoryOption, "-b", "--base-directory", isRequired: false)
        {
        }

        [Fact]
        public void Option_WhenOptionIsMissing_HasDefaultValue()
        {
            ParseResult result = Parse();
            DirectoryInfo? value = result.GetValueForOption(Option);

            VerifyEqual(new DirectoryInfo(Environment.CurrentDirectory), value);
        }

        [Theory]
        [InlineData("directory")]
        [InlineData(@".\directory")]
        public void Option_WhenValueIsNotRooted_HasError(string relativePath)
        {
            VerifyHasError($"{LongOption} {relativePath}");
        }

        [Fact]
        public void Option_WhenValueIsRooted_ParsesValue()
        {
            DirectoryInfo directory = new(".");

            Verify($"{LongOption} \"{directory.FullName}\"", directory);
        }
    }
}