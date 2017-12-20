using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Azure.ActiveDirectory.GraphClient;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;

namespace SignService.Services
{
    public interface IApplicationConfiguration
    {
        Task InitializeAsync();

        string ApplicationObjectId { get; }
        string PrimaryDomain { get; }

        string Location { get; }
    }

    class ApplicationConfiguration : IApplicationConfiguration
    {
        readonly IOptions<AzureAdOptions> azureAdOptions;
        readonly IOptions<ResourceIds> resourceIds;
        readonly IOptions<AdminConfig> adminConfig;
        readonly ILogger<ApplicationConfiguration> logger;

        public ApplicationConfiguration(IOptions<AzureAdOptions> azureAdOptions, IOptions<ResourceIds> resourceIds, IOptions<AdminConfig> adminConfig, ILogger<ApplicationConfiguration> logger)
        {
            this.azureAdOptions = azureAdOptions;
            this.resourceIds = resourceIds;
            this.adminConfig = adminConfig;
            this.logger = logger;
        }

        public async Task InitializeAsync()
        {
            logger.LogDebug("Retrieving application configuration data");

            var authContext = new AuthenticationContext($"{azureAdOptions.Value.AADInstance}{azureAdOptions.Value.TenantId}");
            var clientCredentials = new ClientCredential(azureAdOptions.Value.ClientId, azureAdOptions.Value.ClientSecret);

            
            // Use Graph to populate the Application Object Id and the Primary Domain
            var graphClient = new ActiveDirectoryClient(new Uri($"{adminConfig.Value.GraphInstance}{azureAdOptions.Value.TenantId}"),
                                                        async () =>
                                                        {
                                                            var result = await authContext.AcquireTokenAsync(resourceIds.Value.GraphId, clientCredentials);
                                                            return result.AccessToken;
                                                        });

            

            var tenantDetails = await graphClient.TenantDetails.ExecuteAsync();
            var domains = tenantDetails.CurrentPage.First().VerifiedDomains;

            PrimaryDomain = domains.First(d => d.@default == true).Name;
            logger.LogInformation("Found Primary Domain {PrimaryDomain}", PrimaryDomain);

            var clientId = azureAdOptions.Value.ClientId;
            var app = await graphClient.Applications.Where(a => a.AppId == clientId).ExecuteSingleAsync();

            
            ApplicationObjectId = app.ObjectId;

            logger.LogInformation("Found ApplicationObjectId {ApplicationObjectId} for ClientId {ClientId}", ApplicationObjectId, clientId);
            

            var armAccessToken = await authContext.AcquireTokenAsync(resourceIds.Value.AzureRM, clientCredentials);
            var rgc = new ResourceManagementClient(new TokenCredentials(armAccessToken.AccessToken))
            {
                SubscriptionId = adminConfig.Value.SubscriptionId,
                BaseUri = new Uri(adminConfig.Value.ArmInstance)
            };

            // get the resource group
            var rg = await rgc.ResourceGroups.GetAsync(adminConfig.Value.ResourceGroup);

            Location = rg.Location;

            logger.LogInformation("Found Location {Location} for ResourceGroup {ResourceGroup}", Location, adminConfig.Value.ResourceGroup);
        }

        public string ApplicationObjectId { get; private set; }
        public string PrimaryDomain { get; private set; }
        public string Location { get; private set; }
    }
}
