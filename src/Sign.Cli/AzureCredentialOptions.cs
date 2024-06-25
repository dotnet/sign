// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using Azure.Core;
using Azure.Identity;

namespace Sign.Cli
{
    internal sealed class AzureCredentialOptions
    {
        internal Option<string?> CredentialTypeOption { get; } = new Option<string?>(["--azure-credential-type", "-act"], Resources.CredentialTypeOptionDescription).FromAmong(
            AzureCredentialType.Environment);
        internal Option<bool?> ManagedIdentityOption { get; } = new(["--azure-key-vault-managed-identity", "-kvm"], Resources.ManagedIdentityOptionDescription) { IsHidden = true };
        internal Option<string?> TenantIdOption { get; } = new(["--azure-key-vault-tenant-id", "-kvt"], Resources.TenantIdOptionDescription);
        internal Option<string?> ClientIdOption { get; } = new(["--azure-key-vault-client-id", "-kvi"], Resources.ClientIdOptionDescription);
        internal Option<string?> ClientSecretOption { get; } = new(["--azure-key-vault-client-secret", "-kvs"], Resources.ClientSecretOptionDescription);

        internal void AddOptionsToCommand(Command command)
        {
            command.AddOption(CredentialTypeOption);
            command.AddOption(ManagedIdentityOption);
            command.AddOption(TenantIdOption);
            command.AddOption(ClientIdOption);
            command.AddOption(ClientSecretOption);
        }

        internal DefaultAzureCredentialOptions CreateDefaultAzureCredentialOptions(ParseResult parseResult)
        {
            DefaultAzureCredentialOptions options = new();

            string? credentialType = parseResult.GetValueForOption(CredentialTypeOption);
            if (credentialType is not null)
            {
                options.ExcludeAzureCliCredential = true;
                options.ExcludeAzureDeveloperCliCredential = true;
                options.ExcludeAzurePowerShellCredential = true;
                options.ExcludeEnvironmentCredential = credentialType != AzureCredentialType.Environment;
                options.ExcludeManagedIdentityCredential = true;
                options.ExcludeVisualStudioCredential = true;
                options.ExcludeWorkloadIdentityCredential = true;
            }

            return options;
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

            DefaultAzureCredentialOptions options = CreateDefaultAzureCredentialOptions(context.ParseResult);
            return new DefaultAzureCredential(options);
        }
    }
}
