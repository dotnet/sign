namespace Sign.Cli.Test
{
    public class TimestampUrlOptionTests : UriOptionTests
    {
        public TimestampUrlOptionTests()
            : base(new CodeCommand().TimestampUrlOption, "-t", "--timestamp-url", isRequired: false)
        {
        }
    }
}