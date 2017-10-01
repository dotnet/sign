using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using SignService.Models;
using SignService.Utils;

namespace SignService.Services
{
    public interface IAdminService
    {
        Task<IEnumerable<GraphUser>> GetUsersAsync(string displayName);
        Task<IEnumerable<GraphUser>> GetConfiguredUsersAsync();
        Task RegisterExtensionPropertiesAsync();
        Task UnRegisterExtensionPropertiesAsync();
    }

    public class AdminService : IAdminService
    {
        readonly AdminConfig configuration;
        readonly AzureAdOptions azureAdOptions;
        readonly IGraphHttpService graphHttpService;
        readonly string extensionPrefix;
        
        public AdminService(IOptionsSnapshot<AdminConfig> configuration, IOptionsSnapshot<AzureAdOptions> azureAdOptions, IGraphHttpService graphHttpService)
        {
            this.configuration = configuration.Value;
            this.azureAdOptions = azureAdOptions.Value;
            this.graphHttpService = graphHttpService;
            extensionPrefix = $"extension_{azureAdOptions.Value.ClientId.Replace("-", "")}_";
        }

        public async Task RegisterExtensionPropertiesAsync()
        {
            var extensionProperties = new[]
            {
                new ExtensionProperty
                {
                    DataType = "Boolean",
                    Name = "signServiceConfigured",
                    TargetObjects = new[]
                    {
                        "User"
                    }
                },
                new ExtensionProperty
                {
                    DataType = "String",
                    Name = "keyVaultUrl",
                    TargetObjects = new[]
                    {
                        "User"
                    }
                },
                new ExtensionProperty
                {
                    DataType = "String",
                    Name = "timestampUrl",
                    TargetObjects = new[]
                    {
                        "User"
                    }
                },
                new ExtensionProperty
                {
                    DataType = "String",
                    Name = "keyVaultCertificateName",
                    TargetObjects = new[]
                    {
                        "User"
                    }
                }
            };

            var created = new List<ExtensionProperty>();

            foreach (var prop in extensionProperties)
            {
                var c = await graphHttpService.Post<ExtensionProperty, ExtensionProperty>($"/applications/{azureAdOptions.ApplicationObjectId}/extensionProperties?api-version=1.6", prop).ConfigureAwait(false);
                created.Add(c);
            }
        }

        public async Task UnRegisterExtensionPropertiesAsync()
        {
            var uri = $"/applications/{azureAdOptions.ApplicationObjectId}/extensionProperties?api-version=1.6";

            var result = await graphHttpService.Get<ExtensionProperty>(uri).ConfigureAwait(false);

            foreach (var prop in result)
            {
                await graphHttpService.Delete($"/applications/{azureAdOptions.ApplicationObjectId}/extensionProperties/{prop.ObjectId}?api-version=1.6").ConfigureAwait(false);
            }
        }

        public async Task<IEnumerable<GraphUser>> GetUsersAsync(string displayName)
        {
            displayName = displayName.Replace("'", ""); // don't unescape

            var uri =$"/users?api-version=1.6&$filter=startswith(displayName, '{displayName}') or startswith(givenName, '{displayName}') or startswith(surname, '{displayName}')";
            
            var result = await graphHttpService.Get<GraphUser>(uri).ConfigureAwait(false);
            
            return result;
        }

        public async Task<IEnumerable<GraphUser>> GetConfiguredUsersAsync()
        {
            // This may throw if we run this query before the extension is registered. Just return empty
            try
            {
                var uri = $"/users?api-version=1.6&$filter={extensionPrefix}signServiceConfigured eq true or {extensionPrefix}signServiceConfigured eq false";

                var result = await graphHttpService.Get<GraphUser>(uri).ConfigureAwait(false);

                return result;
            }
            catch (Exception)
            {
                return Enumerable.Empty<GraphUser>();
            }
        }

    }
}
