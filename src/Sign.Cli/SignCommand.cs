// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;

namespace Sign.Cli
{
    internal sealed class SignCommand : Command
    {
        internal SignCommand()
            : base("sign", ".NET Sign CLI")
        {
            CodeCommand codeCommand = new();

            AddCommand(codeCommand);

            AzureKeyVaultCommand azureKeyVaultCommand = new(codeCommand);

            codeCommand.AddCommand(azureKeyVaultCommand);
        }
    }
}