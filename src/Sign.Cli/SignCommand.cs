// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using Sign.Core;

namespace Sign.Cli
{
    internal sealed class SignCommand : RootCommand
    {
        internal SignCommand(IServiceProviderFactory? serviceProviderFactory = null)
            : base(Resources.SignCommandDescription)
        {
            CodeCommand codeCommand = new();
            serviceProviderFactory ??= new ServiceProviderFactory();

            Subcommands.Add(codeCommand);

            AzureKeyVaultCommand azureKeyVaultCommand = new(
                codeCommand,
                serviceProviderFactory);

            codeCommand.Subcommands.Add(azureKeyVaultCommand);

            CertificateStoreCommand certificateStoreCommand = new(
                codeCommand,
                serviceProviderFactory);

            codeCommand.Subcommands.Add(certificateStoreCommand);

            TrustedSigningCommand trustedSigningCommand = new(
                codeCommand,
                serviceProviderFactory);

            codeCommand.Subcommands.Add(trustedSigningCommand);
        }
    }
}
