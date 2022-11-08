namespace Sign.Cli.Test
{
    public class NameOptionTests : OptionTests<string>
    {
        private const string ExpectedValue = "peach";

        public NameOptionTests()
            : base(new CodeCommand().NameOption, "-n", "--name", ExpectedValue, isRequired: true)
        {
        }
    }
}