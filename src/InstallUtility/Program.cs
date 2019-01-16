using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.ActiveDirectory.GraphClient;
using Microsoft.Azure.ActiveDirectory.GraphClient.Extensions;
using Microsoft.Azure.Management.Authorization;
using Microsoft.Azure.Management.Authorization.Models;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.ResourceManager.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using Microsoft.Rest.Azure.OData;
using Newtonsoft.Json;

namespace InstallUtility
{
    class Program
    {
        static IConfiguration configuration;
        static AuthenticationContext authContext;
        static AuthenticationResult authResult;
        static string graphResourceId;
        static string azureRmResourceId;
        const string ClientId = "1b730954-1685-4b74-9bfd-dac224a7b894";
        static readonly Uri RedirectUri = new Uri("urn:ietf:wg:oauth:2.0:oob");
        static ActiveDirectoryClient graphClient;
        static string environment = string.Empty;
        static string serviceRoot = string.Empty;

        const string SignServerName = "SignService Server";
        const string SingClientName = "SignClient App";

        static async Task Main(string[] args)
        {
            configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            graphResourceId = configuration["AzureAd:GraphResourceId"];
            azureRmResourceId = configuration["AzureAd:AzureRmResourceId"];
            authContext = new AuthenticationContext(configuration["AzureAd:Instance"]);

            // Prompt here so we make sure we're in the right directory
            var token = await authContext.AcquireTokenAsync(graphResourceId, ClientId, RedirectUri, new PlatformParameters(PromptBehavior.SelectAccount));
            authResult = token;

            if ("f8cdef31-a31e-4b4a-93e4-5f571e91255a".Equals(token.TenantId, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Microsoft Accounts's with the common endpoint are not supported. Update appsettings.json with your tenant-specific endpoint");
                return;
            }

            serviceRoot = $"{graphResourceId}{token.TenantId}";
            graphClient = new ActiveDirectoryClient(new Uri(serviceRoot), async () => (await authContext.AcquireTokenSilentAsync(graphResourceId, ClientId)).AccessToken);

            if (args.Length > 0)
            {
                // Read a disambiguation value
                environment = $" ({args[0]}) ";
            }

            var serverDisplayNamePrefix = $"{SignServerName}{environment} - ";

            var user = (User)(await graphClient.Me.ExecuteAsync());

            Guid applicationId;
            string password = null;
            // Try to find a "SignService Server -" app
            IPagedCollection<IApplication> a;

            try
            {
                a = await graphClient.Applications.Where(ia => ia.DisplayName.StartsWith(serverDisplayNamePrefix)).ExecuteAsync();
            }
            catch (Exception)
            {
                Console.WriteLine("Guest users are not supported. You must be a member user.");
                return;
            }
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
                // Ensure the Key Vault SP exists, as it might not if a key vault hasn't been created yet
                await EnsureServicePrincipalExists("cfa8b339-82a2-471a-a3c9-0fc0be7a4093", null);

                var appName = $"{serverDisplayNamePrefix}{Guid.NewGuid()}";
                Console.WriteLine($"Creating application '{appName}'");
                // Create
                var newApp = await CreateApplication(appName, user);
                applicationId = newApp.Item1;
                password = newApp.Item2;

                Console.WriteLine("Created application");
            }

            Console.WriteLine("Updating application....");
            var serverApp = await ConfigureApplication(applicationId, user);
            var clientApp = await EnsureClientAppExists(serverApp.application, user);
            Console.WriteLine("Update complete.");

            // If password is not null, we created the app, add the user admin role assignment
            if (!string.IsNullOrWhiteSpace(password))
            {
                await AddAdminRoleAssignmentToServer(serverApp.servicePrincipal);
            }

            // Prompt for consent
            await PromptForConsent(serverApp, clientApp);

            // Need to create a resource group and grant the sign service application the Read permissions
            await CreateOrUpdateResourceGroup(serverApp.servicePrincipal);

            // Print out relevant values
            PrintApplicationInfo(serverApp, password, clientApp);

            Console.WriteLine("Press any key to quit....");
            Console.ReadKey(true);
        }

        static async Task AddAdminRoleAssignmentToServer(IServicePrincipal serverApplication)
        {
            // Get the admin role, 
            var role = serverApplication.AppRoles.First(r => r.Value == "admin_signservice");

            var ara = new AppRoleAssignment
            {
                PrincipalId = Guid.Parse(authResult.UserInfo.UniqueId),
                PrincipalType = "User",
                Id = role.Id,
                ResourceId = Guid.Parse(serverApplication.ObjectId)
            };

            await graphClient.Users.GetByObjectId(authResult.UserInfo.UniqueId).AppRoleAssignments.AddAppRoleAssignmentAsync(ara);
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
            {
                name = "SignService-KeyVaults";
            }

            Console.WriteLine("Location (eastus, westus, etc): ");
            var location = Console.ReadLine();

            var accessToken = await authContext.AcquireTokenSilentAsync(azureRmResourceId, ClientId);

            var rgc = new ResourceManagementClient(new TokenCredentials(accessToken.AccessToken))
            {
                SubscriptionId = subscriptionId,
                BaseUri = new Uri(configuration["AzureRM:Instance"])
            };
            var rg = new ResourceGroup(location, name: name);
            rg = await rgc.ResourceGroups.CreateOrUpdateAsync(name, rg);

            var ac = new AuthorizationManagementClient(new TokenCredentials(accessToken.AccessToken))
            {
                SubscriptionId = subscriptionId,
                BaseUri = new Uri(configuration["AzureRM:Instance"])
            };


            // See if the resource group has the reader role
            // Get the reader role
            var roleFilter = new ODataQuery<RoleDefinitionFilter>(f => f.RoleName == "Reader");

            var roleDefinitions = await ac.RoleDefinitions.ListAsync(rg.Id, roleFilter);
            var roleDefinition = roleDefinitions.First();
            var roleId = roleDefinition.Id;

            var spid = serverApplication.ObjectId;

            var raps = await ac.RoleAssignments.ListForScopeAsync(rg.Id, new ODataQuery<RoleAssignmentFilter>(f => f.PrincipalId == spid));

            if (raps.All(ra => ra.RoleDefinitionId != roleId))
            {
                // none found, add one
                var rap = new RoleAssignmentCreateParameters
                {
                    PrincipalId = spid,
                    RoleDefinitionId = roleId
                };
                var ra = await ac.RoleAssignments.CreateAsync(rg.Id, Guid.NewGuid().ToString(), rap);
            }
        }

        static void PrintApplicationInfo((IApplication application, IServicePrincipal servicePrincipal) server, string password, (IApplication application, IServicePrincipal servicePrincipal) client)
        {
            Console.WriteLine("Sign Server Summary");
            Console.WriteLine("__________________________");
            Console.WriteLine($"DisplayName:\t\t{server.application.DisplayName}");
            Console.WriteLine();

            Console.WriteLine($"Audience:\t\t{server.application.IdentifierUris.First()}");
            Console.WriteLine($"ClientId:\t\t{server.application.AppId}");
            if (password != null)
            {
                Console.WriteLine($"ClientSecret:\t\t{password}");
            }

            Console.WriteLine($"TenantId:\t\t{authResult.TenantId}");
            Console.WriteLine("__________________________");
            Console.WriteLine();


            Console.WriteLine("Sign Client Summary");
            Console.WriteLine("__________________________");
            Console.WriteLine($"DisplayName:\t\t{client.application.DisplayName}");
            Console.WriteLine();

            Console.WriteLine($"ClientId:\t\t{client.application.AppId}");
            Console.WriteLine($"TenantId:\t\t{authResult.TenantId}");
            Console.WriteLine($"Service ResourceId:\t{server.application.IdentifierUris.First()}");
            Console.WriteLine("__________________________");
            Console.WriteLine();
        }

        static async Task PromptForConsent((IApplication application, IServicePrincipal servicePrincipal) server, (IApplication application, IServicePrincipal servicePrincipal) client)
        {
            try
            {
                // Look up the permissions and see if they're already consented. If there's a difference, prompt
                var serverData = await GetApplicationPermissions(server.application);
                var clientData = await GetApplicationPermissions(client.application);

                var perms = await GetPermissionsToAddUpdate(new[]
                {
                (spid: server.servicePrincipal.ObjectId, serverData.permissions),
                (spid: client.servicePrincipal.ObjectId, clientData.permissions)
            });

                var roles = await GetAppRoleAssignmentsToAdd(new[]
                {
                (spid: server.servicePrincipal, permissions: serverData.roles),
                (spid: client.servicePrincipal, permissions: clientData.roles)
            });

                // Nothing to do
                if (perms.Count == 0 && roles.Count == 0)
                {
                    return;
                }

                // Get the friendly text
                var toConsent = serverData.permissions.Concat(clientData.permissions)
                                      .SelectMany(kvp => kvp.Value)
                                      .Select(p => (p.AdminConsentDisplayName))
                                      .ToList();
                var rolesToConsent = serverData.roles.Concat(clientData.roles)
                                .SelectMany(kvp => kvp.Value)
                                .Select(r => r.DisplayName);

                toConsent.AddRange(rolesToConsent);

                toConsent = toConsent.Distinct().ToList();
                toConsent.Sort();

                Console.WriteLine("If you're a directory administrator, you need to grant consent to the service.");
                Console.WriteLine("Either proceed here as an admin or have them grant consent in the Azure Portal");
                Console.WriteLine();

                Console.WriteLine("Required Permissions");
                Console.WriteLine("____________________");
                foreach (var item in toConsent)
                {
                    Console.WriteLine($"{item}");
                }
                Console.WriteLine();

                Console.WriteLine("Do you consent to these permissions on behalf or your organization? [y/N] to continue: ");
                var key = Console.ReadLine()
                                 .ToUpperInvariant()
                                 .Trim();
                if (key != "Y")
                {
                    return;
                }

                // if the objectId on the permission isn't set, it's an add
                foreach (var perm in perms)
                {
                    if (string.IsNullOrWhiteSpace(perm.ObjectId))
                    {
                        await graphClient.Oauth2PermissionGrants.AddOAuth2PermissionGrantAsync(perm);
                    }
                    else
                    {
                        await perm.UpdateAsync();
                    }
                }

                foreach (var role in roles)
                {
                    var sp = (ServicePrincipal)role.Key;
                    foreach (var ara in role.Value)
                    {
                        sp.AppRoleAssignments.Add(ara);
                    }
                    await sp.UpdateAsync();
                }
            }
            catch (Exception)
            {
                Console.WriteLine("Do you do not appear to be a Global Admin. A global admin needs to run this utility or perform additional steps.");
                Console.WriteLine("1. Press \"Grant Permissions\" on the Azure Portal for the two applications ");
                Console.WriteLine("2. Add one or more users as a role on the sign service server application in the Enterprise Apps");
                Console.WriteLine("3. On first run of the admin UI, click \"Register Extension attributes\" in the \"Adv Setup\" area");
            }

        }

        static async Task<Dictionary<IServicePrincipal, List<AppRoleAssignment>>> GetAppRoleAssignmentsToAdd((IServicePrincipal principal, Dictionary<string, List<AppRole>> roles)[] inputs)
        {
            var output = new Dictionary<IServicePrincipal, List<AppRoleAssignment>>();

            foreach (var (principal, roles) in inputs)
            {
                foreach (var kvp in roles)
                {
                    var appid = kvp.Key;
                    var resourceSp = await graphClient.ServicePrincipals.Where(sp => sp.AppId == appid).ExecuteSingleAsync();
                    var resourceSpid = Guid.Parse(resourceSp.ObjectId);

                    // See if the assignment already exists
                    foreach (var role in kvp.Value)
                    {
                        var assn = principal.AppRoleAssignments.CurrentPage.FirstOrDefault(ara => ara.Id == role.Id && ara.ResourceId == resourceSpid);
                        if (assn != null)
                        {
                            continue;
                        }

                        if (!output.TryGetValue(principal, out var list))
                        {
                            list = new List<AppRoleAssignment>();
                            output.Add(principal, list);
                        }

                        var a = new AppRoleAssignment
                        {
                            Id = role.Id,
                            PrincipalId = new Guid(principal.ObjectId),
                            ResourceId = resourceSpid,
                            PrincipalType = "ServicePrincipal"
                        };
                        list.Add(a);
                    }
                }
            }

            return output;
        }

        static async Task<List<IOAuth2PermissionGrant>> GetPermissionsToAddUpdate((string spid, Dictionary<string, List<OAuth2Permission>> permissions)[] inputs)
        {
            var output = new List<IOAuth2PermissionGrant>();
            // Build a list of OAuth2Permission's to add or update

            foreach (var input in inputs)
            {
                var spid = input.spid;
                // See if there's an existing grant and if it has all of the scopes
                var grants = await graphClient.Oauth2PermissionGrants.Where(grant => grant.ClientId == spid && grant.ConsentType == "AllPrincipals").ExecuteAsync();

                foreach (var kvp in input.permissions)
                {
                    // Get the service prinicipal for the resource
                    var appid = kvp.Key;
                    var resourceSp = await graphClient.ServicePrincipals.Where(sp => sp.AppId == appid).ExecuteSingleAsync();

                    var grant = grants.CurrentPage.FirstOrDefault(g => g.ResourceId == resourceSp.ObjectId);

                    if (grant == null)
                    {
                        grant = new OAuth2PermissionGrant
                        {
                            ClientId = spid,
                            ConsentType = "AllPrincipals",
                            ResourceId = resourceSp.ObjectId,
                            Scope = string.Empty,
                            StartTime = DateTime.MinValue,
                            ExpiryTime = DateTime.MaxValue
                        };
                    }

                    // see if we have all the scopes
                    var scopes = grant.Scope.Split(' ');
                    foreach (var scope in kvp.Value)
                    {
                        if (!scopes.Contains(scope.Value))
                        {
                            // something isn't here, add ours and add to output
                            grant.Scope = string.Join(" ", kvp.Value.Select(p => p.Value));
                            output.Add(grant);
                            break;
                        }
                    }
                }
            }

            return output;
        }

        static async Task<(Dictionary<string, List<OAuth2Permission>> permissions, Dictionary<string, List<AppRole>> roles)> GetApplicationPermissions(IApplication application)
        {
            var permissions = new Dictionary<string, List<OAuth2Permission>>();
            var roles = new Dictionary<string, List<AppRole>>();

            foreach (var rra in application.RequiredResourceAccess)
            {
                permissions.Add(rra.ResourceAppId, new List<OAuth2Permission>());
                roles.Add(rra.ResourceAppId, new List<AppRole>());

                var rraid = rra.ResourceAppId;

                var sp = await graphClient.ServicePrincipals.Where(a => a.AppId == rraid).ExecuteSingleAsync();
                foreach (var ra in rra.ResourceAccess)
                {
                    if (ra.Type == "Scope")
                    {
                        var scope = sp.Oauth2Permissions.First(p => p.Id == ra.Id);
                        permissions[rra.ResourceAppId].Add(scope);
                    }
                    else if (ra.Type == "Role")
                    {
                        var role = sp.AppRoles.First(r => r.Id == ra.Id);
                        roles[rra.ResourceAppId].Add(role);
                    }
                }
            }

            return (permissions, roles);
        }

        static async Task<(Guid, string)> CreateApplication(string appName, User owner)
        {
            var randomBytes = new byte[32];
            using (var rnd = RandomNumberGenerator.Create())
            {
                rnd.GetBytes(randomBytes);
            }

            var password = Convert.ToBase64String(randomBytes);

            var application = new Application
            {
                DisplayName = appName,
                Homepage = "https://localhost:44351/",
                ReplyUrls =
                {
                    "https://localhost:44351/signin-oidc"
                },
                PublicClient = false,
                AvailableToOtherTenants = false,
                IdentifierUris =
                {
                    $"https://SignService/{Guid.NewGuid()}"
                },
                PasswordCredentials =
                {
                    new PasswordCredential
                    {
                        CustomKeyIdentifier = Encoding.Unicode.GetBytes("InstallerKey"),
                        Value = password,
                        EndDate = DateTime.UtcNow.AddYears(200)
                    }
                }
            };

            await graphClient.Applications.AddApplicationAsync(application);
            await AddApplicationOwner(application, owner);

            return (Guid.Parse(application.ObjectId), password);
        }

        static async Task AddApplicationOwner(IApplication application, IUser owner)
        {
            await AddOwner("applications", application.ObjectId, owner);
        }

        static async Task AddServicePrincipalOwner(IServicePrincipal servicePrincipal, IUser owner)
        {

            await AddOwner("servicePrincipals", servicePrincipal.ObjectId, owner);
        }

        static async Task AddOwner(string type, string objectId, IUser owner)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", authResult.CreateAuthorizationHeader());

                var payload = $"{{\"url\":\"{serviceRoot}/directoryObjects/{owner.ObjectId}\"}}";
                var stringContent = new StringContent(payload, Encoding.UTF8, "application/json");

                var result = await httpClient.PostAsync($"{serviceRoot}/{type}/{objectId}/$links/owners?api-version=1.6", stringContent);
                result.EnsureSuccessStatusCode();

                var output = await result.Content.ReadAsStringAsync();
            }
        }

        static async Task SetOptionalClaims(OptionalClaims claims, string objectId)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", authResult.CreateAuthorizationHeader());

                var jsonstring = JsonConvert.SerializeObject(claims);

                var payload = $"{{\"optionalClaims\":{jsonstring}}}";

                var msg = new HttpRequestMessage(new HttpMethod("PATCH"), $"{serviceRoot}/applications/{objectId}?api-version=1.6")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };

                var result = await httpClient.SendAsync(msg);
                result.EnsureSuccessStatusCode();

                var output = await result.Content.ReadAsStringAsync();
            }
        }

        static async Task<(IApplication application, IServicePrincipal servicePrincipal)> EnsureClientAppExists(IApplication serviceApplication, User owner)
        {
            // Display Name of the app. The app id is of the sign service it goes to
            var displayName = $"{SingClientName}{environment} - {serviceApplication.AppId}";

            var clientAppSet = await graphClient.Applications.Where(a => a.DisplayName.StartsWith(displayName)).ExecuteAsync();

            var app = clientAppSet.CurrentPage.FirstOrDefault();
            if (app == null)
            {
                app = new Application
                {
                    DisplayName = displayName,
                    ReplyUrls = { "urn:ietf:wg:oauth:2.0:oob" },
                    PublicClient = true,
                    AvailableToOtherTenants = false,
                    Owners = new[] { owner }
                };

                await graphClient.Applications.AddApplicationAsync(app);
                await AddApplicationOwner(app, owner);
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

            var clientSp = await EnsureServicePrincipalExists(app.AppId, owner);

            return (app, clientSp);
        }

        static async Task<(IApplication application, IServicePrincipal servicePrincipal)> ConfigureApplication(Guid appObjId, IUser owner)
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
             *      - Windows Azure Service Management API (797f4846-ba00-4fd7-ba43-dac1f8f63013) OBO scope: (41094075-9dad-400e-a0bd-54e686782033)
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
                (resource:"cfa8b339-82a2-471a-a3c9-0fc0be7a4093", scope: new Guid("f53da476-18e3-4152-8e01-aec403e6edc0"), type: "Scope"),
                (resource:"797f4846-ba00-4fd7-ba43-dac1f8f63013", scope: new Guid("41094075-9dad-400e-a0bd-54e686782033"), type: "Scope")
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

            try
            {
                foreach (var prop in extensionProperties)
                {
                    // See if it exists
                    if (appsExtsList.CurrentPage.FirstOrDefault(ep => ep.Name.EndsWith(prop.Name)) == null)
                    {
                        await appExts.AddExtensionPropertyAsync(prop);
                    }
                }

                // Set optional claims
                var optionalClaims = new OptionalClaims
                {
                    accessToken = (from ep in extensionProperties
                                   select new ClaimInformation
                                   {
                                       name = $"extension_{app.AppId.Replace("-", "")}_{ep.Name}",
                                       source = "user",
                                       essential = true
                                   }).ToArray()
                };

                await SetOptionalClaims(optionalClaims, appObjId.ToString());
            }
            catch (Exception)
            {
                // We'll get here if the user doesn't have permission to create extension attributes
                Console.WriteLine("Warning: You do not have permission to create the required extension attributes, skipped.");
                Console.WriteLine("A Global Admin must access the Admin UI, navigate to 'Adv Setup' and click on 'Register Extension Properties'");
            }

            var serverSp = await EnsureServicePrincipalExists(app.AppId, owner);

            return (app, serverSp);
        }

        static async Task<IServicePrincipal> EnsureServicePrincipalExists(string appid, IUser owner)
        {
            // see if it exists already
            var sc = await graphClient.ServicePrincipals.Where(sp => sp.AppId == appid).Expand(sp => sp.AppRoleAssignments).ExecuteAsync();

            var s = sc.CurrentPage.FirstOrDefault();
            if (s == null)
            {
                // Create it
                s = new ServicePrincipal()
                {
                    AppId = appid,
                    AccountEnabled = true
                };

                await graphClient.ServicePrincipals.AddServicePrincipalAsync(s);

                if (owner != null)
                {
                    await AddServicePrincipalOwner(s, owner);
                }
            }

            return s;
        }
    }
}
