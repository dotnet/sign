// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Cli.Test
{
    public class CertificateOutputOptionTests : OptionTests<string?>
    {
        private const string ExpectedValue = "certificate.cer";

        public CertificateOutputOptionTests()
            : base(new CodeCommand().CertificateOutputOption, "-co", "--certificate-output", ExpectedValue)
        {
        }
    }
}
