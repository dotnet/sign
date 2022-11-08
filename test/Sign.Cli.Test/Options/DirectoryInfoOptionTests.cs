using System.CommandLine;

namespace Sign.Cli.Test
{
    public abstract class DirectoryInfoOptionTests : OptionTests<DirectoryInfo>
    {
        private static readonly DirectoryInfo ExpectedValue = new(Path.GetTempPath());

        protected DirectoryInfoOptionTests(Option<DirectoryInfo> option, string shortOption, string longOption, bool isRequired)
            : base(option, shortOption, longOption, ExpectedValue, isRequired)
        {
        }

        protected override void VerifyEqual(DirectoryInfo? expectedValue, DirectoryInfo? actualValue)
        {
            Assert.Equal(expectedValue?.FullName, actualValue?.FullName);
        }
    }
}