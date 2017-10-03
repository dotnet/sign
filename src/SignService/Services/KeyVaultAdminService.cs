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

        public async Task<IEnumerable<Vault>> ListKeyVaultsAsync()
        {
            var vaults = await kvClient.Vaults.ListByResourceGroupAsync(resourceGroup).ConfigureAwait(false);
            return vaults;
        }

        public async Task<Vault> GetVault(string vaultName)
        {
            var vault = await kvClient.Vaults.GetAsync(resourceGroup, vaultName).ConfigureAwait(false);
            
            return vault;
        }
        
        public async Task<Vault> CreateVault(string vaultName)
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
                        }
                    }
                }
            };
            var vault = await kvClient.Vaults.CreateOrUpdateAsync(resourceGroup, vaultName, parameters).ConfigureAwait(false);

            return vault;
        }

        public async Task<Vault> AssignUserToVault(string vaultName, string objectId, string upn, string displayName)
        {
            var vault = await kvClient.Vaults.GetAsync(resourceGroup, vaultName).ConfigureAwait(false);
            
            var parameters = new VaultCreateOrUpdateParameters(vault.Location, vault.Properties, vault.Tags);

            // See if the user + appid has an ACL
            var acl = parameters.Properties.AccessPolicies.FirstOrDefault(ace => ace.ObjectId == userId && ace.ApplicationId == clientId);
            if (acl == null)
            {
                // Add it
                acl = new AccessPolicyEntry
                {
                    TenantId = tenantId,
                    ObjectId = userId,
                    ApplicationId = clientId,
                    Permissions = new Permissions()
                };
                parameters.Properties.AccessPolicies.Add(acl);
            }

            // Ensure it has the three permissions we need
            // Needs Keys: Get + Sign, Certificates: Get
            if (!acl.Permissions.Keys.Contains("Get", StringComparer.Ordinal))
                acl.Permissions.Keys.Add("Get");
            if (!acl.Permissions.Keys.Contains("Sign", StringComparer.Ordinal))
                acl.Permissions.Keys.Add("Sign");
            if (!acl.Permissions.Certificates.Contains("Get", StringComparer.Ordinal))
                acl.Permissions.Certificates.Add("Get");

            if(parameters.Tags == null)
                parameters.Tags = new Dictionary<string, string>();

            parameters.Tags["userName"] = upn;
            parameters.Tags["displayName"] = displayName;

            var updated = await kvClient.Vaults.CreateOrUpdateAsync(resourceGroup, vaultName, parameters).ConfigureAwait(false);
            return updated;
        }
        
    }
}
