using System.CommandLine;

namespace Sign.Cli.Test
{
    public abstract class Int32OptionTests : OptionTests<int>
    {
        private const int ExpectedValue = 3;

        protected Int32OptionTests(Option<int> option, string shortOption, string longOption, bool isRequired)
            : base(option, shortOption, longOption, ExpectedValue, isRequired)
        {
        }
    }
}