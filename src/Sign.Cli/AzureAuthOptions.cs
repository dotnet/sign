// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using Azure.Core;
using Azure.Identity;
using Sign.Core;

namespace Sign.Cli
{
    internal sealed class AzureAuthOptions
    {
        internal Option<bool> ManagedIdentityOption { get; } = new(["-azm", "--azure-managed-identity"], getDefaultValue: () => false, Resources.ManagedIdentityOptionDescription);
        internal Option<string?> TenantIdOption { get; } = new(["-azt", "--azure-tenant-id"], Resources.TenantIdOptionDescription);
        internal Option<string?> ClientIdOption { get; } = new(["-azi", "--azure-client-id"], Resources.ClientIdOptionDescription);
        internal Option<string?> ClientSecretOption { get; } = new(["-azs", "--azure-client-secret"], Resources.ClientSecretOptionDescription);

        internal void AddOptionsToCommand(Command command)
        {
            command.AddOption(ManagedIdentityOption);
            command.AddOption(TenantIdOption);
            command.AddOption(ClientIdOption);
            command.AddOption(ClientSecretOption);
        }

        internal TokenCredential? CreateTokenCredential(InvocationContext context)
        {
            bool useManagedIdentity = context.ParseResult.GetValueForOption(ManagedIdentityOption);

            if (useManagedIdentity)
            {
                return new DefaultAzureCredential();
            }

            string? tenantId = context.ParseResult.GetValueForOption(TenantIdOption);
            string? clientId = context.ParseResult.GetValueForOption(ClientIdOption);
            string? secret = context.ParseResult.GetValueForOption(ClientSecretOption);

            if (string.IsNullOrEmpty(tenantId) ||
                string.IsNullOrEmpty(clientId) ||
                string.IsNullOrEmpty(secret))
            {
                context.Console.Error.WriteFormattedLine(
                    Resources.InvalidClientSecretCredential,
                    TenantIdOption,
                    ClientIdOption,
                    ClientSecretOption);
                context.ExitCode = ExitCode.NoInputsFound;
                return null;
            }

            return new ClientSecretCredential(tenantId, clientId, secret);
        }
    }
}
