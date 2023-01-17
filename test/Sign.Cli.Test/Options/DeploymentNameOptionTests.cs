// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Cli.Test
{
    public class DeploymentNameOptionTests : OptionTests<string?>
    {
        private const string? ExpectedValue = "peach";

        public DeploymentNameOptionTests()
            : base(new CodeCommand().DeploymentNameOption, "-dn", "--deployment-name", ExpectedValue)
        {
        }
    }
}