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
        Task<VaultModel> CreateVaultForUserAsync(string objectId, string upn, string displayName);
        Task<VaultModel> GetVaultAsync(string vaultName);
        Task<List<VaultModel>> ListKeyVaultsAsync();
    }

    public class KeyVaultAdminService : IKeyVaultAdminService
    {
        readonly AuthenticationContext adalContext;
        readonly string resourceGroup;
        readonly AzureAdOptions azureAdOptions;
        readonly AdminConfig adminConfig;
        readonly Guid tenantId;
        readonly Guid clientId;
        readonly KeyVaultManagementClient kvClient;

        public KeyVaultAdminService(IOptionsSnapshot<AzureAdOptions> azureAdOptions, IOptionsSnapshot<AdminConfig> adminConfig, IHttpContextAccessor contextAccessor)
        {
            var principal = contextAccessor.HttpContext.User;
            var userId = principal.FindFirst("oid").Value;
            tenantId = Guid.Parse(principal.FindFirst("tid").Value);
            clientId = Guid.Parse(azureAdOptions.Value.ClientId);

            adalContext = new AuthenticationContext($"{azureAdOptions.Value.AADInstance}{azureAdOptions.Value.TenantId}", new ADALSessionCache(userId, contextAccessor));
            resourceGroup = adminConfig.Value.ResourceGroup;
            kvClient = new KeyVaultManagementClient(new KeyVaultCredential(GetAppToken));
            kvClient.SubscriptionId = adminConfig.Value.SubscriptionId;

            this.azureAdOptions = azureAdOptions.Value;
            this.adminConfig = adminConfig.Value;
        }

        async Task<string> GetAppToken(string authority, string resource, string scope)
        {
            var result = await adalContext.AcquireTokenAsync("https://management.core.windows.net/", new ClientCredential(azureAdOptions.ClientId, azureAdOptions.ClientSecret)).ConfigureAwait(false);

            return result.AccessToken;
        }

        public async Task<List<VaultModel>> ListKeyVaultsAsync()
        {
            var totalVaults = new List<Vault>();
            var vaults = await kvClient.Vaults.ListByResourceGroupAsync(resourceGroup)
                                       .ConfigureAwait(false);

            totalVaults.AddRange(vaults);
            var nextLink = vaults.NextPageLink;

            // Get the rest if there's more
            while (!string.IsNullOrWhiteSpace(nextLink))
            {
                vaults = await kvClient.Vaults.ListByResourceGroupNextAsync(nextLink)
                                       .ConfigureAwait(false);
                totalVaults.AddRange(vaults);
                nextLink = vaults.NextPageLink;
            }

            return totalVaults.Select(ToVaultModel).ToList();
        }

        public async Task<VaultModel> GetVaultAsync(string vaultName)
        {
            var vault = await kvClient.Vaults.GetAsync(resourceGroup, vaultName).ConfigureAwait(false);
            
            return ToVaultModel(vault);
        }
        
        public async Task<VaultModel> CreateVaultForUserAsync(string objectId, string upn, string displayName)
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
                        // Grant this application admin permissions on the management plane to deal with keys and certificates
                        new AccessPolicyEntry
                        {
                          ObjectId = azureAdOptions.ApplicationObjectId,
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
                            ObjectId = objectId,
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
            return ToVaultModel(vault);
        }
        static VaultModel ToVaultModel(Vault vault)
        {
            string dname = null;
            string username = null;
            var model = new VaultModel
            {
                Id = vault.Id,
                DisplayName = vault.Tags?.TryGetValue("displayName", out dname) == true ? dname : null,
                Username = vault.Tags?.TryGetValue("userName", out username) == true ? username : null,
                Type = vault.Type,
                Name = vault.Name,
                Location = vault.Location
            };

            return model;
        }
    }
}
