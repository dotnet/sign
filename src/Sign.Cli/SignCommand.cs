// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using Sign.Core;

namespace Sign.Cli
{
    internal sealed class SignCommand : Command
    {
        internal SignCommand(IServiceProviderFactory? serviceProviderFactory = null)
            : base("sign", Resources.SignCommandDescription)
        {
            CodeCommand codeCommand = new();

            AddCommand(codeCommand);

            AzureKeyVaultCommand azureKeyVaultCommand = new(
                codeCommand,
                serviceProviderFactory ?? new ServiceProviderFactory());

            codeCommand.AddCommand(azureKeyVaultCommand);
        }
    }
}