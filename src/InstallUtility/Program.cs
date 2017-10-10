using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.ActiveDirectory.GraphClient;
using Microsoft.Azure.Management.Authorization;
using Microsoft.Azure.Management.Authorization.Models;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.ResourceManager.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using Microsoft.Rest.Azure.OData;

namespace InstallUtility
{
    class Program
    {
        static IConfiguration configuration;
        static AuthenticationContext authContext;
        static AuthenticationResult authResult;
        static string graphResourceId;
        static string azureRmResourceId;
        const string clientId = "1b730954-1685-4b74-9bfd-dac224a7b894";
        static readonly Uri redirectUri = new Uri("urn:ietf:wg:oauth:2.0:oob");
        static ActiveDirectoryClient graphClient;
        static string environment = string.Empty;
        static async Task Main(string[] args)
        {
            configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();
            
            graphResourceId = configuration["AzureAd:GraphResourceId"];
            azureRmResourceId = configuration["AzureAd:AzureRmResourceId"];
            authContext = new AuthenticationContext($"{configuration["AzureAd:Instance"]}common");

            // Prompt here so we make sure we're in the right directory
            var token = await authContext.AcquireTokenAsync(graphResourceId, clientId, redirectUri, new PlatformParameters(PromptBehavior.Auto));
            authResult = token;
            graphClient = new ActiveDirectoryClient(new Uri($"{graphResourceId}{token.TenantId}"), async () => (await authContext.AcquireTokenSilentAsync(graphResourceId, clientId)).AccessToken);
            
            if (args.Length > 0)
            {
                // Read a disambiguation value
                environment = $" ({args[0]}) ";
            }

            var serverDisplayNamePrefix = $"SignService Server{environment} - ";


            Guid applicationId;

            // Try to find a "SignService Server -" app
            var a = await graphClient.Applications.Where(ia => ia.DisplayName.StartsWith(serverDisplayNamePrefix)).ExecuteAsync();
            if (a.CurrentPage.Count == 1)
            {
                applicationId = Guid.Parse(a.CurrentPage[0].ObjectId);
                Console.WriteLine($"Found application '{a.CurrentPage[0].DisplayName}'");
                Console.WriteLine("Enter [Y/n] to continue: ");
                var key = Console.ReadLine().ToUpperInvariant().Trim();
                if (!(key == string.Empty || key == "Y"))
                {
                    Console.WriteLine("Exiting....");
                    return;
                }
            }
            else
            {
                var appName = $"{serverDisplayNamePrefix}{Guid.NewGuid()}";
                Console.WriteLine($"Creating application '{appName}'");
                // Create
                applicationId = await CreateApplication(appName);

                Console.WriteLine("Created application");
            }
            
            Console.WriteLine("Updating application....");
            var apps = await ConfigureApplication(applicationId);
            Console.WriteLine("Update complete.");

            // Need to create a resource group and grant the sign service application the Read permissions
            await CreateOrUpdateResourceGroup(apps.serverServicePrincipal);

            // Print out relevant values
            PrintApplicationInfo(apps);

            Console.WriteLine("Press any key to quit....");
            Console.ReadKey(true);
        }

        static async Task CreateOrUpdateResourceGroup(IServicePrincipal serverApplication)
        {

            Console.WriteLine("Add or update Key Vault Resource Group (required once)? [y/N] to continue: ");
            var key = Console.ReadLine()
                             .ToUpperInvariant()
                             .Trim();
            if (key != "Y")
            {
                return;
            }

            Console.Write("SubscriptionId: ");
            var subscriptionId = Console.ReadLine();
            Console.Write("Resource Group Name (blank for default 'SignService-KeyVaults'): ");
            var name = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(name))
                name = "SignService-KeyVaults";
            Console.WriteLine("Location (eastus, westus, etc): ");
            var location = Console.ReadLine();

            var accessToken = await authContext.AcquireTokenSilentAsync(azureRmResourceId, clientId);

            var rgc = new ResourceManagementClient(new TokenCredentials(accessToken.AccessToken));
            rgc.SubscriptionId = subscriptionId;
            var rg = new ResourceGroup(location, name: name);
            rg = await rgc.ResourceGroups.CreateOrUpdateAsync(name, rg);
            
            var ac = new AuthorizationManagementClient(new TokenCredentials(accessToken.AccessToken));
            ac.SubscriptionId = subscriptionId;


            // See if the resource group has the reader role
            // Get the reader role
            var roleFilter = new ODataQuery<RoleDefinitionFilter>(f => f.RoleName == "Reader");

            var roleDefinitions = await ac.RoleDefinitions.ListAsync(rg.Id, roleFilter);
            var roleDefinition = roleDefinitions.First();
            var roleId = roleDefinition.Id;

            var spid = serverApplication.ObjectId;

            var raps = await ac.RoleAssignments.ListForScopeAsync(rg.Id, new ODataQuery<RoleAssignmentFilter>(f => f.PrincipalId == spid));

            if (raps.All(ra => ra.Properties.RoleDefinitionId != roleId))
            {
                // none found, add one
                var rap = new RoleAssignmentProperties
                {
                    PrincipalId = spid,
                    RoleDefinitionId = roleId
                };
                var ra = await ac.RoleAssignments.CreateAsync(rg.Id, Guid.NewGuid().ToString(), rap);
            }
        }

        static void PrintApplicationInfo((IApplication server, IServicePrincipal serverServicePrincipal, IApplication client) apps)
        {
            Console.WriteLine("Sign Server Summary");
            Console.WriteLine("__________________________");
            Console.WriteLine($"DisplayName:\t\t{apps.server.DisplayName}");
            Console.WriteLine();
            
            Console.WriteLine($"Audience:\t\t{apps.server.IdentifierUris.First()}");
            Console.WriteLine($"ClientId:\t\t{apps.server.AppId}");
            Console.WriteLine($"TenantId:\t\t{authResult.TenantId}");
            Console.WriteLine($"ApplicationObjectId:\t{apps.server.ObjectId}");
            Console.WriteLine("__________________________");
            Console.WriteLine();


            Console.WriteLine("Sign Client Summary");
            Console.WriteLine("__________________________");
            Console.WriteLine($"DisplayName:\t\t{apps.client.DisplayName}");
            Console.WriteLine();
            
            Console.WriteLine($"ClientId:\t\t{apps.client.AppId}");
            Console.WriteLine($"TenantId:\t\t{authResult.TenantId}");
            Console.WriteLine($"Service ResourceId:\t{apps.server.IdentifierUris.First()}");
            Console.WriteLine("__________________________");
            Console.WriteLine();
        }

      
        static async Task<Guid> CreateApplication(string appName)
        {
            var application = new Application
            {
                DisplayName = appName,
                Homepage = "https://localhost:44351/",
                ReplyUrls = { "https://localhost:44351/signin-oidc" },
                PublicClient = false,
                AvailableToOtherTenants = false,
                IdentifierUris = { $"https://SignService/{Guid.NewGuid()}" }
            };

            await graphClient.Applications.AddApplicationAsync(application);
            return Guid.Parse(application.ObjectId);
        }

        static async Task<IApplication> EnsureClientAppExists(IApplication serviceApplication)
        {
            // Display Name of the app. The app id is of the sign service it goes to
            var displayName = $"SignClient App{environment} - {serviceApplication.AppId}";

            var clientAppSet = await graphClient.Applications.Where(a => a.DisplayName.StartsWith(displayName)).ExecuteAsync();

            var app = clientAppSet.CurrentPage.FirstOrDefault();
            if (app == null)
            {
                app = new Application
                {
                    DisplayName = displayName,
                    ReplyUrls = { "urn:ietf:wg:oauth:2.0:oob" },
                    PublicClient = true,
                    AvailableToOtherTenants = false
                };
                await graphClient.Applications.AddApplicationAsync(app);
            }

            // Get the user_impersonation scope from the service
            var uis = serviceApplication.Oauth2Permissions.First(oa => oa.Value == "user_impersonation");
            
            // Check to see if it has teh required resource access to the sign service
            var resource = app.RequiredResourceAccess.FirstOrDefault(rr => rr.ResourceAppId == serviceApplication.AppId);
            if (resource == null)
            {
                resource = new RequiredResourceAccess
                {
                    ResourceAppId = serviceApplication.AppId
                };
                app.RequiredResourceAccess.Add(resource);
            }

            // Check the scope
            var resAccess = resource.ResourceAccess.FirstOrDefault(ra => ra.Id == uis.Id);
            if (resAccess == null)
            {
                resAccess = new ResourceAccess
                {
                    Id = uis.Id,
                    Type = "Scope"
                };
                resource.ResourceAccess.Add(resAccess);
            }

            await app.UpdateAsync();

            var clientSp = EnsureServicePrinicpalExists(app);

            return app;
        }

        static async Task<(IApplication server, IServicePrincipal serverServicePrincipal, IApplication client)> ConfigureApplication(Guid appObjId)
        {
            
            var appFetcher = graphClient.Applications.GetByObjectId(appObjId.ToString());
            
            var app = await appFetcher.ExecuteAsync();
            var appExts = appFetcher.ExtensionProperties;
            var appsExtsList = await appExts.ExecuteAsync();

            /* We need to add a few things to the application entry. We leave the redirect URI to the portal for now,
             * though that should be https://host/signin-oidc
             * 
             * 1. App Role for Admin
             * 2. Resource Access for the following
             *      - Azure Graph (00000002-0000-0000-c000-000000000000) with the following scopes: 
             *          UserProfile.Read (311a71cc-e848-46a1-bdf8-97ff7156d8e6), 
             *          Directory.ReadWrite.All (78c8a3c8-a07e-4b9e-af1b-b5ccab50a175),
             *          Directory.AccessAsUser.All (a42657d6-7f20-40e3-b6f0-cee03008a62a)
             *      - Key Vault app (cfa8b339-82a2-471a-a3c9-0fc0be7a4093) OBO scope: (f53da476-18e3-4152-8e01-aec403e6edc0)
             * 3. Register the four extension properties the app uses for storing data on the service account users
             * 
             */

            // See if the app role is missing and add 
            if (app.AppRoles.All(ar => ar.Value != "admin_signservice"))
            {
                app.AppRoles.Add(new AppRole
                {
                    Value = "admin_signservice",
                    Description = "Admin SignService",
                    DisplayName = "Admin SignService",
                    IsEnabled = true,
                    Id = Guid.NewGuid(),
                    AllowedMemberTypes = { "User" }
                });
            }

            // Check for scopes and add if missing
            var requiredResourceAccess = new[] 
            {
                (resource:"00000002-0000-0000-c000-000000000000", scope: new Guid("311a71cc-e848-46a1-bdf8-97ff7156d8e6"), type: "Scope"),
                (resource:"00000002-0000-0000-c000-000000000000", scope: new Guid("a42657d6-7f20-40e3-b6f0-cee03008a62a"), type: "Scope"),
                (resource:"00000002-0000-0000-c000-000000000000", scope: new Guid("78c8a3c8-a07e-4b9e-af1b-b5ccab50a175"), type: "Role"),
                (resource:"cfa8b339-82a2-471a-a3c9-0fc0be7a4093", scope: new Guid("f53da476-18e3-4152-8e01-aec403e6edc0"), type: "Scope")
            };

            foreach (var rra in requiredResourceAccess)
            {
                // See if we have the resource id
                var resource = app.RequiredResourceAccess.FirstOrDefault(rr => rr.ResourceAppId == rra.resource);
                if (resource == null)
                {
                    resource = new RequiredResourceAccess
                    {
                        ResourceAppId = rra.resource
                    };
                    app.RequiredResourceAccess.Add(resource);
                }

                // Now look for the scope in the resource
                var resAccess = resource.ResourceAccess.FirstOrDefault(ra => ra.Id == rra.scope);
                if (resAccess == null)
                {
                    resAccess = new ResourceAccess
                    {
                        Id = rra.scope,
                        Type = rra.type
                    };
                    resource.ResourceAccess.Add(resAccess);
                }
            }


            await app.UpdateAsync();


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

            foreach (var prop in extensionProperties)
            {
                // See if it exists
                if(appsExtsList.CurrentPage.FirstOrDefault(ep => ep.Name.EndsWith(prop.Name)) == null)
                {
                    await appExts.AddExtensionPropertyAsync(prop);
                }
            }

            var serverSp = await EnsureServicePrinicpalExists(app);

            var client = await EnsureClientAppExists(app);
            return (app, serverSp, client);
        }

        async static Task<IServicePrincipal> EnsureServicePrinicpalExists(IApplication application)
        {
            // see if it exists already
            var appid = application.AppId;
            var sc = await graphClient.ServicePrincipals.Where(sp => sp.AppId == appid).ExecuteAsync();

            var s = sc.CurrentPage.FirstOrDefault();
            if (s == null)
            {
                // Create it
                s = new ServicePrincipal()
                {
                    AppId = appid,
                    AccountEnabled = true,
                    Tags = { "WindowsAzureActiveDirectoryIntegratedApp" } // This is what VS does...
                };

                await graphClient.ServicePrincipals.AddServicePrincipalAsync(s);
            }

            return s;
        }
    }
}
