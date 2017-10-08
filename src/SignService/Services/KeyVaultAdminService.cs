using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
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
        Task<List<CertificateModel>> GetCertificatesInVaultAsync(string vaultUri);
    }

    public class KeyVaultAdminService : IKeyVaultAdminService
    {
        readonly AuthenticationContext adalContext;
        readonly string resourceGroup;
        readonly AzureAdOptions azureAdOptions;
        readonly AdminConfig adminConfig;
        readonly Guid tenantId;
        readonly Guid clientId;
        readonly string userId;
        readonly KeyVaultManagementClient kvManagmentClient;
        readonly KeyVaultClient kvClient;
        readonly IGraphHttpService graphHttpService;
        readonly Resources resources;

        public KeyVaultAdminService(IOptionsSnapshot<AzureAdOptions> azureAdOptions, IOptionsSnapshot<AdminConfig> adminConfig, IOptionsSnapshot<Resources> resources, IGraphHttpService graphHttpService, IHttpContextAccessor contextAccessor)
        {
            var principal = contextAccessor.HttpContext.User;
            userId = principal.FindFirst("oid").Value;
            tenantId = Guid.Parse(principal.FindFirst("tid").Value);
            clientId = Guid.Parse(azureAdOptions.Value.ClientId);

            adalContext = new AuthenticationContext($"{azureAdOptions.Value.AADInstance}{azureAdOptions.Value.TenantId}", new ADALSessionCache(userId, contextAccessor));
            resourceGroup = adminConfig.Value.ResourceGroup;
            kvManagmentClient = new KeyVaultManagementClient(new KeyVaultCredential(GetAppToken));
            kvManagmentClient.SubscriptionId = adminConfig.Value.SubscriptionId;
            kvClient = new KeyVaultClient(new KeyVaultCredential(GetAppTokenForKv));

            this.azureAdOptions = azureAdOptions.Value;
            this.adminConfig = adminConfig.Value;
            this.graphHttpService = graphHttpService;
            this.resources = resources.Value;
        }

        async Task<string> GetAppToken(string authority, string resource, string scope)
        {
            var result = await adalContext.AcquireTokenAsync(resources.AzureRM, new ClientCredential(azureAdOptions.ClientId, azureAdOptions.ClientSecret)).ConfigureAwait(false);

            return result.AccessToken;
        }

        async Task<string> GetAppTokenForKv(string authority, string resource, string scope)
        {
            var result = await adalContext.AcquireTokenAsync(resources.VaultId, new ClientCredential(azureAdOptions.ClientId, azureAdOptions.ClientSecret)).ConfigureAwait(false);

            return result.AccessToken;
        }

        async Task<string> GetOboToken(string authority, string resource, string scope)
        {
            var result = await adalContext.AcquireTokenSilentAsync(resources.AzureRM, new ClientCredential(azureAdOptions.ClientId, azureAdOptions.ClientSecret), UserIdentifier.AnyUser).ConfigureAwait(false);

            return result.AccessToken;
        }

        public async Task<List<VaultModel>> ListKeyVaultsAsync()
        {
            var totalVaults = new List<Vault>();
            var vaults = await kvManagmentClient.Vaults.ListByResourceGroupAsync(resourceGroup)
                                       .ConfigureAwait(false);

            totalVaults.AddRange(vaults);
            var nextLink = vaults.NextPageLink;

            // Get the rest if there's more
            while (!string.IsNullOrWhiteSpace(nextLink))
            {
                vaults = await kvManagmentClient.Vaults.ListByResourceGroupNextAsync(nextLink)
                                       .ConfigureAwait(false);
                totalVaults.AddRange(vaults);
                nextLink = vaults.NextPageLink;
            }

            return totalVaults.Select(ToVaultModel).ToList();
        }

        public async Task<VaultModel> GetVaultAsync(string vaultName)
        {
            var vault = await kvManagmentClient.Vaults.GetAsync(resourceGroup, vaultName).ConfigureAwait(false);
            
            return ToVaultModel(vault);
        }
        
        public async Task<VaultModel> CreateVaultForUserAsync(string objectId, string upn, string displayName)
        {
            // Get the service principal id for this application since we'll need it
            var spId = await graphHttpService.GetValue<string>($"/servicePrincipalsByAppId/{azureAdOptions.ClientId}/objectId?api-version=1.6");

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
                          ObjectId = spId,
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

                        // Grant the current user the management plane access for use in the portal/powershell for 
                        // manual tasks
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

            // Create uses an OBO so that this only works if the user has contributer+ access to the resource group
            using (var client = new KeyVaultManagementClient(new KeyVaultCredential(GetOboToken)))
            {
                client.SubscriptionId = adminConfig.SubscriptionId;
                var vault = await client.Vaults.CreateOrUpdateAsync(resourceGroup, vaultName, parameters).ConfigureAwait(false);
                
                return ToVaultModel(vault);
            }
        }

        public async Task<List<CertificateModel>> GetCertificatesInVaultAsync(string vaultUri)
        {
            var totalItems = new List<CertificateItem>();
            var certs = await kvClient.GetCertificatesAsync(vaultUri).ConfigureAwait(false);

            totalItems.AddRange(certs);
            var nextLink = certs.NextPageLink;

            // Get the rest if there's more
            while (!string.IsNullOrWhiteSpace(nextLink))
            {
                certs = await kvClient.GetCertificatesNextAsync(nextLink).ConfigureAwait(false);
                totalItems.AddRange(certs);
                nextLink = certs.NextPageLink;
            }

            return totalItems.Select(ci => new CertificateModel
            {
                Name =  ci.Id.Substring(ci.Id.LastIndexOf("/") + 1),
                CertificateIdentifier = ci.Identifier.Identifier,
                Thumbprint = BitConverter.ToString(ci.X509Thumbprint).Replace("-", ""),
                Attributes = ci.Attributes
            }).OrderBy(cm => cm.Name).ToList();
        }

        public async Task<X509Certificate2> GetCertificateDetails(string certificateIdentifier)
        {
            var bundle = await kvClient.GetCertificateAsync(certificateIdentifier).ConfigureAwait(false);
            return new X509Certificate2(bundle.Cer);
        }

        static VaultModel ToVaultModel(Vault vault)
        {
            string dname = null;
            string username = null;
            var model = new VaultModel
            {
                VaultUri = vault.Properties.VaultUri,
                DisplayName = vault.Tags?.TryGetValue("displayName", out dname) == true ? dname : null,
                Username = vault.Tags?.TryGetValue("userName", out username) == true ? username : null,
                Name = vault.Name,
                Location = vault.Location
            };

            return model;
        }
    }
}
