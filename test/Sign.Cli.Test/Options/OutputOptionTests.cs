namespace Sign.Cli.Test
{
    public class OutputOptionTests : OptionTests<string?>
    {
        private const string ExpectedValue = "peach";

        public OutputOptionTests()
            : base(new CodeCommand().OutputOption, "-o", "--output", ExpectedValue, isRequired: false)
        {
        }
    }
}