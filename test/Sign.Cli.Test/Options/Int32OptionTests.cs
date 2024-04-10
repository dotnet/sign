// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;

namespace Sign.Cli.Test
{
    public abstract class Int32OptionTests : OptionTests<int>
    {
        private const int ExpectedValue = 3;

        protected Int32OptionTests(Option<int> option, string shortOption, string longOption)
            : base(option, shortOption, longOption, ExpectedValue)
        {
        }
    }
}