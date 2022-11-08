namespace Sign.Cli.Test
{
    public class PublisherNameOptionTests : OptionTests<string?>
    {
        private const string? ExpectedValue = "peach";

        public PublisherNameOptionTests()
            : base(new CodeCommand().PublisherNameOption, "-pn", "--publisher-name", ExpectedValue, isRequired: false)
        {
        }
    }
}