using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.ActiveDirectory.GraphClient;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

namespace InstallUtility
{
    class Program
    {
        static IConfiguration configuration;
        static AuthenticationContext authContext;
        static string graphResourceId;
        const string clientId = "1b730954-1685-4b74-9bfd-dac224a7b894";
        static readonly Uri redirectUri = new Uri("urn:ietf:wg:oauth:2.0:oob");

        static async Task Main(string[] args)
        {
            configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            if (args.Length == 0)
            {
                Console.WriteLine("Create an application entry in AAD to populate and supply the 'Object ID' in the cmd line");
                return;
            }

            var applicationId = Guid.Parse(args[0]);

            graphResourceId = configuration["AzureAd:GraphResourceId"];
            authContext = new AuthenticationContext($"{configuration["AzureAd:Instance"]}common");

            // Prompt here so we make sure we're in the right directory
            var token = await authContext.AcquireTokenAsync(graphResourceId, clientId, redirectUri, new PlatformParameters(PromptBehavior.Auto));

            Console.WriteLine("Updating application....");
            await ConfigureApplication(token.TenantId, applicationId, token.AccessToken);
            Console.WriteLine("Update complete.");
        }

        static async Task ConfigureApplication(string tenantId, Guid appObjId, string accessToken)
        {
            var gc = new ActiveDirectoryClient(new Uri($"{graphResourceId}{tenantId}"), async () => (await authContext.AcquireTokenSilentAsync(graphResourceId, clientId)).AccessToken);

            var appFetcher = gc.Applications.GetByObjectId(appObjId.ToString());
            
            var app = await appFetcher.ExecuteAsync();
            var appExts = appFetcher.ExtensionProperties;
            var appsExtsList = await appExts.ExecuteAsync();

            /* We need to add a few things to the application entry. We leave the redirect URI to the portal for now,
             * though that should be https://host/signin-oidc
             * 
             * 1. App Role for Admin
             * 2. Resource Access for the following
             *      - Azure Graph (00000002-0000-0000-c000-000000000000) with the following scopes: UserProfile.Read (311a71cc-e848-46a1-bdf8-97ff7156d8e6), Directory.ActAsUser.All (a42657d6-7f20-40e3-b6f0-cee03008a62a)
             *      - Key Vault app (cfa8b339-82a2-471a-a3c9-0fc0be7a4093) OBO scope: (f53da476-18e3-4152-8e01-aec403e6edc0)
             *      - Azure Service Management API App: (797f4846-ba00-4fd7-ba43-dac1f8f63013) Scope (41094075-9dad-400e-a0bd-54e686782033)
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
                (resource:"00000002-0000-0000-c000-000000000000", scope: new Guid("311a71cc-e848-46a1-bdf8-97ff7156d8e6")),
                (resource:"00000002-0000-0000-c000-000000000000", scope: new Guid("a42657d6-7f20-40e3-b6f0-cee03008a62a")),
                (resource:"cfa8b339-82a2-471a-a3c9-0fc0be7a4093", scope: new Guid("f53da476-18e3-4152-8e01-aec403e6edc0")),
                (resource:"797f4846-ba00-4fd7-ba43-dac1f8f63013", scope: new Guid("41094075-9dad-400e-a0bd-54e686782033"))
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
                        Type = "Scope"
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

            Console.WriteLine("foo");
            
        }
    }
}
