// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using Azure.Core;
using Azure.Identity;

namespace Sign.Cli
{
    internal sealed class AzureCredentialOptions
    {
        internal Option<string?> CredentialTypeOption { get; }
        internal Option<string?> ManagedIdentityClientIdOption { get; }
        internal Option<string?> ManagedIdentityResourceIdOption { get; }
        internal Option<bool?> ObsoleteManagedIdentityOption { get; }
        internal Option<string?> ObsoleteTenantIdOption { get; }
        internal Option<string?> ObsoleteClientIdOption { get; }
        internal Option<string?> ObsoleteClientSecretOption { get; }

        internal AzureCredentialOptions()
        {
            CredentialTypeOption = new Option<string?>("--azure-credential-type", "-act")
            {
                Description = Resources.CredentialTypeOptionDescription
            };
            CredentialTypeOption.AcceptOnlyFromAmong(
                AzureCredentialType.AzureCli,
                AzureCredentialType.AzurePowerShell,
                AzureCredentialType.ManagedIdentity,
                AzureCredentialType.WorkloadIdentity);

            ManagedIdentityClientIdOption = new Option<string?>("--managed-identity-client-id", "-mici")
            {
                Description = Resources.ManagedIdentityClientIdOptionDescription
            };
            ManagedIdentityResourceIdOption = new Option<string?>("--managed-identity-resource-id", "-miri")
            {
                Description = Resources.ManagedIdentityResourceIdOptionDescription
            };
            ObsoleteManagedIdentityOption = new Option<bool?>("--azure-key-vault-managed-identity", "-kvm")
            {
                Description = Resources.ManagedIdentityOptionDescription,
                Hidden = true
            };
            ObsoleteTenantIdOption = new Option<string?>("--azure-key-vault-tenant-id", "-kvt")
            {
                Description = Resources.TenantIdOptionDescription,
                Hidden = true
            };
            ObsoleteClientIdOption = new Option<string?>("--azure-key-vault-client-id", "-kvi")
            {
                Description = Resources.ClientIdOptionDescription,
                Hidden = true
            };
            ObsoleteClientSecretOption = new Option<string?>("--azure-key-vault-client-secret", "-kvs")
            {
                Description = Resources.ClientSecretOptionDescription,
                Hidden = true
            };
        }

        internal void AddOptionsToCommand(Command command)
        {
            command.Options.Add(CredentialTypeOption);
            command.Options.Add(ManagedIdentityClientIdOption);
            command.Options.Add(ManagedIdentityResourceIdOption);
            command.Options.Add(ObsoleteManagedIdentityOption);
            command.Options.Add(ObsoleteTenantIdOption);
            command.Options.Add(ObsoleteClientIdOption);
            command.Options.Add(ObsoleteClientSecretOption);
        }

        internal DefaultAzureCredentialOptions CreateDefaultAzureCredentialOptions(ParseResult parseResult)
        {
            DefaultAzureCredentialOptions options = new();

            string? managedIdentityClientId = parseResult.GetValue(ManagedIdentityClientIdOption);
            if (managedIdentityClientId is not null)
            {
                options.ManagedIdentityClientId = managedIdentityClientId;
            }

            string? managedIdentityResourceId = parseResult.GetValue(ManagedIdentityResourceIdOption);
            if (managedIdentityResourceId is not null)
            {
                options.ManagedIdentityResourceId = new ResourceIdentifier(managedIdentityResourceId);
            }

            return options;
        }

        internal TokenCredential? CreateTokenCredential(ParseResult parseResult)
        {
            bool? useManagedIdentity = parseResult.GetValue(ObsoleteManagedIdentityOption);

            if (useManagedIdentity is not null)
            {
                Console.Out.WriteLine(Resources.ManagedIdentityOptionObsolete);
            }

            string? tenantId = parseResult.GetValue(ObsoleteTenantIdOption);
            string? clientId = parseResult.GetValue(ObsoleteClientIdOption);
            string? secret = parseResult.GetValue(ObsoleteClientSecretOption);

            if (!string.IsNullOrEmpty(tenantId) &&
                !string.IsNullOrEmpty(clientId) &&
                !string.IsNullOrEmpty(secret))
            {
                Console.Out.WriteLine(Resources.ClientSecretOptionsObsolete);
                return new ClientSecretCredential(tenantId, clientId, secret);
            }

            switch (parseResult.GetValue(CredentialTypeOption))
            {
                case AzureCredentialType.AzureCli:
                    return new AzureCliCredential();

                case AzureCredentialType.AzurePowerShell:
                    return new AzurePowerShellCredential();

                case AzureCredentialType.ManagedIdentity:
                    string? managedIdentityClientId = parseResult.GetValue(ManagedIdentityClientIdOption);
                    if (managedIdentityClientId is not null)
                    {
                        return new ManagedIdentityCredential(managedIdentityClientId);
                    }

                    string? managedIdentityResourceId = parseResult.GetValue(ManagedIdentityResourceIdOption);
                    if (managedIdentityResourceId is not null)
                    {
                        return new ManagedIdentityCredential(new ResourceIdentifier(managedIdentityResourceId));
                    }

                    return new ManagedIdentityCredential();

                case AzureCredentialType.WorkloadIdentity:
                    return new WorkloadIdentityCredential();

                default:
                    DefaultAzureCredentialOptions options = CreateDefaultAzureCredentialOptions(parseResult);

                    // CodeQL [SM05137] Sign CLI is not a production service.
                    return new DefaultAzureCredential(options);
            }
        }
    }
}
