// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Diagnostics;
using Azure.Core;
using Azure.Identity;
using Microsoft.Identity.Client.Region;

namespace Sign.Cli
{
    internal sealed class AzureCredentialOptions
    {
        internal Option<string?> CredentialTypeOption { get; } = new Option<string?>(["--azure-credential-type", "-act"], Resources.CredentialTypeOptionDescription).FromAmong(
            AzureCredentialType.Default,
            AzureCredentialType.AzureCli,
            AzureCredentialType.Environment,
            AzureCredentialType.ManagedIdentity);
        internal Option<string?> TenantIdOption { get; } = new(["--azure-tenant-id", "-ati"], Resources.TenantIdOptionDescription);
        internal Option<string?> ManagedIdentityClientIdOption = new(["--managed-identity-client-id", "-mici"], Resources.ManagedIdentityClientIdOptionDescription);
        internal Option<string?> ManagedIdentityResourceIdOption = new(["--managed-identity-resource-id", "-miri"], Resources.ManagedIdentityResourceIdOptionDescription);
        internal Option<bool?> ObsoleteManagedIdentityOption { get; } = new(["--azure-key-vault-managed-identity", "-kvm"], Resources.ManagedIdentityOptionDescription) { IsHidden = true };
        internal Option<string?> ObsoleteTenantIdOption { get; } = new(["--azure-key-vault-tenant-id", "-kvt"], Resources.TenantIdOptionDescription) { IsHidden = true };
        internal Option<string?> ObsoleteClientIdOption { get; } = new(["--azure-key-vault-client-id", "-kvi"], Resources.ClientIdOptionDescription) { IsHidden = true };
        internal Option<string?> ObsoleteClientSecretOption { get; } = new(["--azure-key-vault-client-secret", "-kvs"], Resources.ClientSecretOptionDescription) { IsHidden = true };

        internal void AddOptionsToCommand(Command command)
        {
            command.AddOption(CredentialTypeOption);
            command.AddOption(TenantIdOption);
            command.AddOption(ManagedIdentityClientIdOption);
            command.AddOption(ManagedIdentityResourceIdOption);
            command.AddOption(ObsoleteManagedIdentityOption);
            command.AddOption(ObsoleteTenantIdOption);
            command.AddOption(ObsoleteClientIdOption);
            command.AddOption(ObsoleteClientSecretOption);
        }

        internal TokenCredential? CreateTokenCredential(InvocationContext context)
        {
            Debugger.Launch();

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
            
            tenantId = context.ParseResult.GetValueForOption(TenantIdOption);
            string? managedIdentityResourceId = context.ParseResult.GetValueForOption(ManagedIdentityResourceIdOption);
            string? managedIdentityClientId = context.ParseResult.GetValueForOption(ManagedIdentityClientIdOption);

            var credentialType = context.ParseResult.GetValueForOption(CredentialTypeOption);
            switch (credentialType)
            {
                case AzureCredentialType.AzureCli:
                    return new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId });

                case AzureCredentialType.Environment:
                    return new EnvironmentCredential();

                case AzureCredentialType.ManagedIdentity when managedIdentityResourceId is not null:
                    return new ManagedIdentityCredential(
                        resourceId: new ResourceIdentifier(managedIdentityResourceId));
                case AzureCredentialType.ManagedIdentity when managedIdentityClientId is not null:
                    return new ManagedIdentityCredential(
                        clientId: managedIdentityClientId);
                case AzureCredentialType.ManagedIdentity:
                    return new ManagedIdentityCredential();

                case AzureCredentialType.Default:
                case null:
                    return new DefaultAzureCredential(new DefaultAzureCredentialOptions
                    {
                        TenantId = tenantId,
                        ManagedIdentityResourceId = managedIdentityResourceId is not null
                            ? new ResourceIdentifier(managedIdentityResourceId) : null,
                        ManagedIdentityClientId = managedIdentityResourceId is null
                            ? managedIdentityClientId : null,
                    });

                default:
                    throw new NotImplementedException("Credential type not supported: " + credentialType);
            }
        }
    }
}
