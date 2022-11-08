namespace Sign.Cli.Test
{
    public class DescriptionUrlOptionTests : UriOptionTests
    {
        public DescriptionUrlOptionTests()
            : base(new CodeCommand().DescriptionUrlOption, "-u", "--description-url", isRequired: true)
        {
        }
    }
}