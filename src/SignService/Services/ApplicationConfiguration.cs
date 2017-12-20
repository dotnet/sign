using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Azure.ActiveDirectory.GraphClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

namespace SignService.Services
{
    public interface IApplicationConfiguration
    {
        Task InitializeAsync();

        string ApplicationObjectId { get; }
        string PrimaryDomain { get; }
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



        }

        public string ApplicationObjectId { get; private set; }
        public string PrimaryDomain { get; private set; }
    }
}
