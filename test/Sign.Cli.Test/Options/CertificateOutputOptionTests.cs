// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Moq;
using Sign.Core;

namespace Sign.Cli.Test
{
    public class CertificateOutputOptionTests : OptionTests<string?>
    {
        private const string ExpectedValue = "certificate.cer";

        public CertificateOutputOptionTests()
            : base(
                new ArtifactSigningCommand(new CodeCommand(), Mock.Of<IServiceProviderFactory>()).CertificateOutputOption,
                "-co",
                "--certificate-output",
                ExpectedValue)
        {
        }
    }
}
