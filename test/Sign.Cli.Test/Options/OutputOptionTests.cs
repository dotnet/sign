// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Cli.Test
{
    public class OutputOptionTests : OptionTests<string?>
    {
        private const string ExpectedValue = "peach";

        public OutputOptionTests()
            : base(new CodeCommand().OutputOption, "-o", "--output", ExpectedValue)
        {
        }
    }
}