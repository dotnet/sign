﻿// Licensed to the .NET Foundation under one or more agreements.
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
        internal Option<string?> CredentialTypeOption = new Option<string?>(["--azure-credential-type", "-act"], Resources.CredentialTypeOptionDescription).FromAmong(
            AzureCredentialType.AzureCli,
            AzureCredentialType.AzurePowerShell,
            AzureCredentialType.ManagedIdentity,
            AzureCredentialType.WorkloadIdentity);
        internal Option<string?> ManagedIdentityClientIdOption = new(["--managed-identity-client-id", "-mici"], Resources.ManagedIdentityClientIdOptionDescription);
        internal Option<string?> ManagedIdentityResourceIdOption = new(["--managed-identity-resource-id", "-miri"], Resources.ManagedIdentityResourceIdOptionDescription);
        internal Option<bool?> ObsoleteManagedIdentityOption { get; } = new(["--azure-key-vault-managed-identity", "-kvm"], Resources.ManagedIdentityOptionDescription) { IsHidden = true };
        internal Option<string?> ObsoleteTenantIdOption { get; } = new(["--azure-key-vault-tenant-id", "-kvt"], Resources.TenantIdOptionDescription) { IsHidden = true };
        internal Option<string?> ObsoleteClientIdOption { get; } = new(["--azure-key-vault-client-id", "-kvi"], Resources.ClientIdOptionDescription) { IsHidden = true };
        internal Option<string?> ObsoleteClientSecretOption { get; } = new(["--azure-key-vault-client-secret", "-kvs"], Resources.ClientSecretOptionDescription) { IsHidden = true };

        internal void AddOptionsToCommand(Command command)
        {
            command.AddOption(CredentialTypeOption);
            command.AddOption(ManagedIdentityClientIdOption);
            command.AddOption(ManagedIdentityResourceIdOption);
            command.AddOption(ObsoleteManagedIdentityOption);
            command.AddOption(ObsoleteTenantIdOption);
            command.AddOption(ObsoleteClientIdOption);
            command.AddOption(ObsoleteClientSecretOption);
        }

        internal DefaultAzureCredentialOptions CreateDefaultAzureCredentialOptions(ParseResult parseResult)
        {
            DefaultAzureCredentialOptions options = new();

            string? managedIdentityClientId = parseResult.GetValueForOption(ManagedIdentityClientIdOption);
            if (managedIdentityClientId is not null)
            {
                options.ManagedIdentityClientId = managedIdentityClientId;
            }

            string? managedIdentityResourceId = parseResult.GetValueForOption(ManagedIdentityResourceIdOption);
            if (managedIdentityResourceId is not null)
            {
                options.ManagedIdentityResourceId = new ResourceIdentifier(managedIdentityResourceId);
            }

            return options;
        }

        internal TokenCredential? CreateTokenCredential(InvocationContext context)
        {
            bool? useManagedIdentity = context.ParseResult.GetValueForOption(ObsoleteManagedIdentityOption);

            if (useManagedIdentity is not null)
            {
                context.Console.Out.WriteLine(Resources.ManagedIdentityOptionObsolete);
            }

            string? tenantId = context.ParseResult.GetValueForOption(ObsoleteTenantIdOption);
            string? clientId = context.ParseResult.GetValueForOption(ObsoleteClientIdOption);
            string? secret = context.ParseResult.GetValueForOption(ObsoleteClientSecretOption);

            if (!string.IsNullOrEmpty(tenantId) &&
                !string.IsNullOrEmpty(clientId) &&
                !string.IsNullOrEmpty(secret))
            {
                context.Console.Out.WriteLine(Resources.ClientSecretOptionsObsolete);
                return new ClientSecretCredential(tenantId, clientId, secret);
            }

            switch (context.ParseResult.GetValueForOption(CredentialTypeOption))
            {
                case AzureCredentialType.AzureCli:
                    return new AzureCliCredential();

                case AzureCredentialType.AzurePowerShell:
                    return new AzurePowerShellCredential();

                case AzureCredentialType.ManagedIdentity:
                    string? managedIdentityClientId = context.ParseResult.GetValueForOption(ManagedIdentityClientIdOption);
                    if (managedIdentityClientId is not null)
                    {
                        return new ManagedIdentityCredential(managedIdentityClientId);
                    }

                    string? managedIdentityResourceId = context.ParseResult.GetValueForOption(ManagedIdentityResourceIdOption);
                    if (managedIdentityResourceId is not null)
                    {
                        return new ManagedIdentityCredential(new ResourceIdentifier(managedIdentityResourceId));
                    }

                    return new ManagedIdentityCredential();

                case AzureCredentialType.WorkloadIdentity:
                    return new WorkloadIdentityCredential();

                default:
                    DefaultAzureCredentialOptions options = CreateDefaultAzureCredentialOptions(context.ParseResult);

                    // CodeQL [SM05137] Sign CLI is not a production service.
                    return new DefaultAzureCredential(options);
            }
        }
    }
}
