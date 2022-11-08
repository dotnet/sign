namespace Sign.Cli.Test
{
    public class FileDigestOptionTests : HashAlgorithmNameOptionTests
    {
        public FileDigestOptionTests()
            : base(new CodeCommand().FileDigestOption, "-fd", "--file-digest", isRequired: false)
        {
        }
    }
}