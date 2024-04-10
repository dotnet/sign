// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;

namespace Sign.Cli.Test
{
    public abstract class DirectoryInfoOptionTests : OptionTests<DirectoryInfo>
    {
        private static readonly DirectoryInfo ExpectedValue = new(Path.GetTempPath());

        protected DirectoryInfoOptionTests(Option<DirectoryInfo> option, string shortOption, string longOption)
            : base(option, shortOption, longOption, ExpectedValue)
        {
        }

        protected override void VerifyEqual(DirectoryInfo? expectedValue, DirectoryInfo? actualValue)
        {
            Assert.Equal(expectedValue?.FullName, actualValue?.FullName);
        }
    }
}