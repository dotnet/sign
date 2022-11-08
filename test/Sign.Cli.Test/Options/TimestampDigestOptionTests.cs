namespace Sign.Cli.Test
{
    public class TimestampDigestOptionTests : HashAlgorithmNameOptionTests
    {
        public TimestampDigestOptionTests()
            : base(new CodeCommand().TimestampDigestOption, "-td", "--timestamp-digest", isRequired: false)
        {
        }
    }
}