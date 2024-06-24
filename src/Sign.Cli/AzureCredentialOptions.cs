// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Azure.Core;
using Azure.Identity;

namespace Sign.Cli
{
    internal sealed class AzureCredentialOptions
    {
        internal Option<bool?> ManagedIdentityOption { get; } = new(["-kvm", "--azure-key-vault-managed-identity"], Resources.ManagedIdentityOptionDescription);
        internal Option<string?> TenantIdOption { get; } = new(["-kvt", "--azure-key-vault-tenant-id"], Resources.TenantIdOptionDescription);
        internal Option<string?> ClientIdOption { get; } = new(["-kvi", "--azure-key-vault-client-id"], Resources.ClientIdOptionDescription);
        internal Option<string?> ClientSecretOption { get; } = new(["-kvs", "--azure-key-vault-client-secret"], Resources.ClientSecretOptionDescription);

        internal void AddOptionsToCommand(Command command)
        {
            command.AddOption(ManagedIdentityOption);
            command.AddOption(TenantIdOption);
            command.AddOption(ClientIdOption);
            command.AddOption(ClientSecretOption);
        }

        internal TokenCredential? CreateTokenCredential(InvocationContext context)
        {
            bool? useManagedIdentity = context.ParseResult.GetValueForOption(ManagedIdentityOption);

            if (useManagedIdentity is not null)
            {
                context.Console.Out.WriteLine(Resources.ManagedIdentityOptionObsolete);
            }

            string? tenantId = context.ParseResult.GetValueForOption(TenantIdOption);
            string? clientId = context.ParseResult.GetValueForOption(ClientIdOption);
            string? secret = context.ParseResult.GetValueForOption(ClientSecretOption);

            if (!string.IsNullOrEmpty(tenantId) &&
                !string.IsNullOrEmpty(clientId) &&
                !string.IsNullOrEmpty(secret))
            {
                return new ClientSecretCredential(tenantId, clientId, secret);
            }

            return new DefaultAzureCredential();
        }
    }
}
