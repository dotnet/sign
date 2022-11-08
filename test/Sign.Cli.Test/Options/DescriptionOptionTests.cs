namespace Sign.Cli.Test
{
    public class DescriptionOptionTests : OptionTests<string>
    {
        private const string ExpectedValue = "peach";

        public DescriptionOptionTests()
            : base(new CodeCommand().DescriptionOption, "-d", "--description", ExpectedValue, isRequired: true)
        {
        }
    }
}