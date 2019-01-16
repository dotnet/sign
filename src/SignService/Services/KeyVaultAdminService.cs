using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.AzureAD.UI;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Management.KeyVault;
using Microsoft.Azure.Management.KeyVault.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using SignService.Models;
using SignService.Utils;

namespace SignService.Services
{
    public interface IKeyVaultAdminService
    {
        Task<VaultModel> CreateVaultForUserAsync(string objectId, string upn, string displayName);
        Task<VaultModel> GetVaultAsync(string vaultName);
        Task<List<VaultModel>> ListKeyVaultsAsync();
        Task<List<CertificateModel>> GetCertificatesInVaultAsync(string vaultUri);
        Task<CertificateOperation> CancelCsrAsync(string vaultName, string certificateName);
        Task<CertificateBundle> MergeCertificate(string vaultName, string certificateName, byte[] certData);
        Task<CertificateOperation> GetCertificateOperation(string vaultUrl, string certificateName);
        Task<CertificateOperation> CreateCsrAsync(string vaultName, string certificateName, string displayName);
    }

    public class KeyVaultAdminService : IKeyVaultAdminService
    {
        readonly AuthenticationContext adalContext;
        readonly string resourceGroup;
        readonly AzureADOptions azureAdOptions;
        readonly AdminConfig adminConfig;
        readonly Guid tenantId;
        readonly Guid clientId;
        readonly string userId;
        readonly KeyVaultManagementClient kvManagmentClient;
        readonly KeyVaultClient kvClient;
        readonly IGraphHttpService graphHttpService;
        readonly IApplicationConfiguration applicationConfiguration;
        readonly ResourceIds resources;

        public KeyVaultAdminService(IOptionsSnapshot<AzureADOptions> azureAdOptions,
                                    IOptionsSnapshot<AdminConfig> adminConfig,
                                    IOptionsSnapshot<ResourceIds> resources,
                                    IGraphHttpService graphHttpService,
                                    IApplicationConfiguration applicationConfiguration,
                                    IUser user,
                                    IHttpContextAccessor contextAccessor)
        {
            userId = user.ObjectId;
            tenantId = Guid.Parse(user.TenantId);
            this.azureAdOptions = azureAdOptions.Get(AzureADDefaults.AuthenticationScheme);

            clientId = Guid.Parse(this.azureAdOptions.ClientId);

            adalContext = new AuthenticationContext($"{this.azureAdOptions.Instance}{this.azureAdOptions.TenantId}", new ADALSessionCache(userId, contextAccessor));
            resourceGroup = adminConfig.Value.ResourceGroup;

            kvManagmentClient = new KeyVaultManagementClient(new AutoRestCredential<KeyVaultManagementClient>(GetAppToken))
            {
                SubscriptionId = adminConfig.Value.SubscriptionId,
                BaseUri = new Uri(adminConfig.Value.ArmInstance)

            };
            kvClient = new KeyVaultClient(new AutoRestCredential<KeyVaultClient>(GetAppTokenForKv));

            
            this.adminConfig = adminConfig.Value;
            this.graphHttpService = graphHttpService;
            this.applicationConfiguration = applicationConfiguration;
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
                Location = applicationConfiguration.Location,
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
                                  "DeleteIssuers",
                                  "Backup",
                                  "Restore",
                                  "Recover"
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
                                    "DeleteIssuers",
                                    "Backup",
                                    "Restore",
                                    "Recover"
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
            using (var client = new KeyVaultManagementClient(new AutoRestCredential<KeyVaultManagementClient>(GetOboToken)))
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

            // Get keys since they may be there for pending certs
            var totalKeys = new List<KeyItem>();
            var keys = await kvClient.GetKeysAsync(vaultUri).ConfigureAwait(false);
            totalKeys.AddRange(keys);

            nextLink = keys.NextPageLink;
            // Get the rest if there's more
            while (!string.IsNullOrWhiteSpace(nextLink))
            {
                keys = await kvClient.GetKeysNextAsync(nextLink).ConfigureAwait(false);
                totalKeys.AddRange(keys);
                nextLink = keys.NextPageLink;
            }

            // only get the ones where we don't have a cert
            var keyDict = totalKeys.ToDictionary(ki => ki.Kid.Substring(ki.Kid.LastIndexOf("/") + 1));

            var models = totalItems
                .Select(ci => new CertificateModel
                {
                    Name = ci.Id.Substring(ci.Id.LastIndexOf("/") + 1),
                    CertificateIdentifier = ci.Identifier.Identifier,
                    Thumbprint = BitConverter.ToString(ci.X509Thumbprint).Replace("-", ""),
                    Attributes = ci.Attributes
                }).ToList();

            foreach (var model in models)
            {
                keyDict.Remove(model.Name);
            }

            models.AddRange(keyDict.Select(kvp => new CertificateModel
            {
                Name = kvp.Key
            }));

            return models.OrderBy(cm => cm.Name).ToList();
        }

        public async Task<CertificateOperation> GetCertificateOperation(string vaultUrl, string certificateName)
        {
            try
            {
                var op = await kvClient.GetCertificateOperationAsync(vaultUrl, certificateName).ConfigureAwait(false);
                return op;

            } // May not be any pending operations
            catch (KeyVaultErrorException e) when (e.Response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<CertificateOperation> CreateCsrAsync(string vaultName, string certificateName, string displayName)
        {
            var policy = new CertificatePolicy()
            {
                X509CertificateProperties = new X509CertificateProperties
                {
                    Subject = $"CN={displayName}"
                },
                KeyProperties = new KeyProperties
                {
                    KeySize = 2048,
                    KeyType = "RSA-HSM"
                },
                IssuerParameters = new IssuerParameters
                {
                    Name = "Unknown" // External CA
                }
            };

            var vault = await GetVaultAsync(vaultName).ConfigureAwait(false);
            var op = await kvClient.CreateCertificateAsync(vault.VaultUri, certificateName, policy).ConfigureAwait(false);
            return op;
        }

        public async Task<CertificateOperation> CancelCsrAsync(string vaultName, string certificateName)
        {
            var vault = await GetVaultAsync(vaultName).ConfigureAwait(false);
            var op = await kvClient.UpdateCertificateOperationAsync(vault.VaultUri, certificateName, true).ConfigureAwait(false);
            op = await kvClient.DeleteCertificateOperationAsync(vault.VaultUri, certificateName).ConfigureAwait(false);
            return op;
        }

        public async Task<CertificateBundle> MergeCertificate(string vaultName, string certificateName, byte[] certData)
        {
            // Get an X509CCertificate2Collection from the cert data
            // this supports either P7b or CER
            var publicCertificates = CryptoUtil.GetCertificatesFromCryptoData(certData);

            var vault = await GetVaultAsync(vaultName).ConfigureAwait(false);
            var op = await kvClient.MergeCertificateAsync(vault.VaultUri, certificateName, publicCertificates).ConfigureAwait(false);
            return op;
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
