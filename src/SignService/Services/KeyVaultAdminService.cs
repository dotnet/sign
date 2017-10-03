using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Management.KeyVault;
using Microsoft.Extensions.Options;
using SignService.Utils;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using SignService.Models;
using Microsoft.Azure.Management.KeyVault.Models;

namespace SignService.Services
{
    public interface IKeyVaultAdminService
    {
        Task<Vault> CreateVaultForUserAsync(string objectId, string upn, string displayName);
        Task<Vault> GetVaultAsync(string vaultName);
        Task<List<Vault>> ListKeyVaultsAsync();
    }

    public class KeyVaultAdminService : IKeyVaultAdminService
    {
        readonly AuthenticationContext adalContext;
        readonly KeyVaultManagementClient kvClient;
        readonly string resourceGroup;
        readonly AzureAdOptions azureAdOptions;
        readonly AdminConfig adminConfig;
        readonly Guid tenantId;
        readonly string userId;
        readonly Guid clientId;

        public KeyVaultAdminService(IOptionsSnapshot<AzureAdOptions> azureAdOptions, IOptionsSnapshot<AdminConfig> adminConfig, IHttpContextAccessor contextAccessor)
        {
            var principal = contextAccessor.HttpContext.User;

            userId = principal.FindFirst("oid").Value;
            tenantId = Guid.Parse(principal.FindFirst("tid").Value);
            clientId = Guid.Parse(azureAdOptions.Value.ClientId);

            adalContext = new AuthenticationContext($"{azureAdOptions.Value.AADInstance}{azureAdOptions.Value.TenantId}", new ADALSessionCache(userId, contextAccessor));
            resourceGroup = adminConfig.Value.ResourceGroup;

            kvClient = new KeyVaultManagementClient(new KeyVaultCredential(async (authority, resource, scope) => (await adalContext.AcquireTokenSilentAsync(resource, azureAdOptions.Value.ClientId)).AccessToken));

            kvClient.SubscriptionId = adminConfig.Value.SubscriptionId;
            this.azureAdOptions = azureAdOptions.Value;
            this.adminConfig = adminConfig.Value;
        }

        public async Task<List<Vault>> ListKeyVaultsAsync()
        {
            var totalVaults = new List<Vault>();
            var vaults = await kvClient.Vaults.ListByResourceGroupAsync(resourceGroup).ConfigureAwait(false);

            totalVaults.AddRange(vaults);
            var nextLink = vaults.NextPageLink;

            // Get the rest if there's more
            while (!string.IsNullOrWhiteSpace(nextLink))
            {
                vaults = await kvClient.Vaults.ListByResourceGroupNextAsync(nextLink).ConfigureAwait(false);
                totalVaults.AddRange(vaults);
                nextLink = vaults.NextPageLink;
            }

            return totalVaults;
        }

        public async Task<Vault> GetVaultAsync(string vaultName)
        {
            var vault = await kvClient.Vaults.GetAsync(resourceGroup, vaultName).ConfigureAwait(false);
            
            return vault;
        }
        
        public async Task<Vault> CreateVaultForUserAsync(string objectId, string upn, string displayName)
        {
            var parameters = new VaultCreateOrUpdateParameters()
            {
                Location = adminConfig.Location,
                Properties = new VaultProperties()
                {
                    TenantId = tenantId,
                    Sku = new Sku(SkuName.Premium),
                    AccessPolicies = new List<AccessPolicyEntry>
                    {
                        // Grant the user who created it admin permissions on the management plane to deal with keys and certificates
                        new AccessPolicyEntry
                        {
                          ObjectId = userId,
                          TenantId = tenantId,
                          Permissions  = new Permissions
                          {
                              Keys = new List<string>
                              {
                                  "Backup",
                                  "Create",
                                  "Delete",
                                  "Get",
                                  "Import",
                                  "List",
                                  "Restore",
                                  "Update",
                                  "Recover"
                              },
                              Certificates = new List<string>
                              {
                                  "Get",
                                  "List",
                                  "Update",
                                  "Create",
                                  "Import",
                                  "Delete",
                                  "ManageContacts",
                                  "ManageIssuers",
                                  "GetIssuers",
                                  "ListIssuers",
                                  "SetIssuers",
                                  "DeleteIssuers"
                              }
                          }
                        },
                        // Needs Keys: Get + Sign, Certificates: Get
                        new AccessPolicyEntry
                        {
                            TenantId = tenantId,
                            ObjectId = userId,
                            ApplicationId = clientId,
                            Permissions  = new Permissions
                            {
                                Keys = new List<string>
                                {
                                    "Get",
                                    "Sign",
                                },
                                Certificates = new List<string>
                                {
                                    "Get"
                                }
                            }
                        }
                    }
                },
                Tags = new Dictionary<string, string>
                {
                    {"userName", upn },
                    {"displayName", displayName }
                },
            };

            // for the vault name, we get up to 24 characters, so use the following:
            // upn up to the @ then a dash then fill with a guid truncated
            var vaultName = $"{upn.Substring(0, upn.IndexOf('@'))}-{Guid.NewGuid().ToString("N")}";

            // Truncate to 24 chars
            vaultName = vaultName.Substring(0, 24);

            var vault = await kvClient.Vaults.CreateOrUpdateAsync(resourceGroup, vaultName, parameters).ConfigureAwait(false);
            return vault;
        }
        
    }
}
