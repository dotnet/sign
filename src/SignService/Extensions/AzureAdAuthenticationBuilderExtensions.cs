using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SignService;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Newtonsoft.Json;
using SignService.Models;
using SignService.Services;
using SignService.Utils;

namespace Microsoft.AspNetCore.Authentication
{
    public static class AzureAdServiceCollectionExtensions
    {
        public static AuthenticationBuilder AddAzureAdBearer(this AuthenticationBuilder builder, Action<AzureAdOptions> configureOptions)
        {
            builder.Services.Configure(configureOptions);
            builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureAzureOptions>();
            builder.AddJwtBearer();
            return builder;
        }

        public static AuthenticationBuilder AddAzureAd(this AuthenticationBuilder builder, Action<AzureAdOptions> configureOptions)
        {
         //   builder.Services.Configure(configureOptions);
            builder.Services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, ConfigureAzureOidcOptions>();
            builder.Services.AddSingleton<IConfigureOptions<CookieAuthenticationOptions>, ConfigureCookieOptions>();

            builder.AddOpenIdConnect();
            return builder;
        }

        private class ConfigureAzureOptions : IConfigureNamedOptions<JwtBearerOptions>
        {
            readonly AzureAdOptions _azureOptions;
            readonly IOptions<AdminConfig> adminOptions;
            readonly IHttpContextAccessor contextAccessor;

            public ConfigureAzureOptions(IOptions<AzureAdOptions> azureOptions, IOptions<Settings> settings, IOptions<AdminConfig> adminOptions, IHttpContextAccessor contextAccessor)
            {
                _azureOptions = azureOptions.Value;
                this.adminOptions = adminOptions;
                this.contextAccessor = contextAccessor;
            }

            public void Configure(string name, JwtBearerOptions options)
            {
                options.Audience = _azureOptions.Audience;
                options.Authority = $"{_azureOptions.AADInstance}{_azureOptions.TenantId}";
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = OnTokenValidated
                };
                options.TokenValidationParameters.RoleClaimType = "roles";
            }

            async Task OnTokenValidated(JwtBearer.TokenValidatedContext tokenValidatedContext)
            {
                var passed = false;

                var identity = (ClaimsIdentity)tokenValidatedContext.Principal.Identity;

                // See if there's a UPN, and if so, use that object id
                var upn = identity.Claims.FirstOrDefault(c => c.Type == "upn")?.Value;
                if (upn != null)
                {
                    var oid = identity.Claims.FirstOrDefault(c => c.Type == "oid")?.Value;

                    // get the user
                    var context = new AuthenticationContext($"{_azureOptions.AADInstance}{_azureOptions.TenantId}", null); // No token caching
                    var credential = new ClientCredential(_azureOptions.ClientId, _azureOptions.ClientSecret);
                    var resource = "https://graph.windows.net";
                    var incomingToken = ((JwtSecurityToken)tokenValidatedContext.SecurityToken).RawData;
                    var result = await context.AcquireTokenAsync(resource, credential, new UserAssertion(incomingToken));

                    var url = $"{adminOptions.Value.GraphInstance}{_azureOptions.TenantId}/users/{oid}?api-version=1.6";
                    GraphUser user = null;
                    using (var client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
                        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        var resp = await client.GetAsync(url).ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode)
                        {
                            user = JsonConvert.DeserializeObject<GraphUser>(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        }
                    }

                    if (user?.SignServiceConfigured == true)
                    {
                        passed = true;
                        
                        identity.AddClaim(new Claim("keyVaultUrl", user.KeyVaultUrl));
                        identity.AddClaim(new Claim("keyVaultCertificateName", user.KeyVaultCertificateName));
                        identity.AddClaim(new Claim("timestampUrl", user.TimestampUrl));
                        identity.AddClaim(new Claim("access_token", incomingToken));
                    }
                }

                if (!passed)
                {
                    // If we get here, it's an unknown value
                    tokenValidatedContext.Fail("User is not configured");
                }
            }

            public void Configure(JwtBearerOptions options)
            {
                Configure(Options.DefaultName, options);
            }
        }

        private class ConfigureCookieOptions : IConfigureNamedOptions<CookieAuthenticationOptions>
        {
            private readonly IOptions<AzureAdOptions> azureOptions;
            private readonly IHttpContextAccessor contextAccessor;

            public ConfigureCookieOptions(IOptions<AzureAdOptions> azureOptions, IHttpContextAccessor contextAccessor)
            {
                this.azureOptions = azureOptions;
                this.contextAccessor = contextAccessor;
            }
            public void Configure(string name, CookieAuthenticationOptions options)
            {
                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = context =>
                    {
                        var userId = context.Principal.FindFirst("oid").Value;

                        // Check if exists in ADAL cache and reject if not. This happens if the cookie is alive and the server bounced
                        var adal = new AuthenticationContext($"{azureOptions.Value.AADInstance}{azureOptions.Value.TenantId}", new ADALSessionCache(userId, contextAccessor));
                        if (adal.TokenCache.Count == 0)
                        {
                            context.RejectPrincipal();
                        }
                        return Task.CompletedTask; ;
                    }
                };
            }

            public void Configure(CookieAuthenticationOptions options)
            {
                Configure(Options.DefaultName, options);
            }
        }

        private class ConfigureAzureOidcOptions : IConfigureNamedOptions<OpenIdConnectOptions>
        {
            private readonly AzureAdOptions _azureOptions;
            private readonly IHttpContextAccessor contextAccessor;

            public ConfigureAzureOidcOptions(IOptions<AzureAdOptions> azureOptions, IHttpContextAccessor contextAccessor)
            {
                _azureOptions = azureOptions.Value;
                this.contextAccessor = contextAccessor;
            }

            public void Configure(string name, OpenIdConnectOptions options)
            {
                options.ClientId = _azureOptions.ClientId;
                options.Authority = $"{_azureOptions.AADInstance}{_azureOptions.TenantId}";
                options.UseTokenLifetime = true;
                options.CallbackPath = _azureOptions.CallbackPath;
                options.RequireHttpsMetadata = true;
                options.TokenValidationParameters.RoleClaimType = "roles";
                options.TokenValidationParameters.NameClaimType = "name";
                options.ResponseType = OpenIdConnectResponseType.CodeIdToken;
                options.Scope.Add("offline_access");
                options.Events = new OpenIdConnectEvents
                {
                    OnAuthorizationCodeReceived = OnAuthorizationCodeReceived
                };
            }

            async Task OnAuthorizationCodeReceived(AuthorizationCodeReceivedContext context)
            {
                var userId = context.Principal.FindFirst("oid").Value;
                
                
                var adal = new AuthenticationContext($"{_azureOptions.AADInstance}{_azureOptions.TenantId}", new ADALSessionCache(userId, contextAccessor));

                var redirect = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}";
                // Store in cache for later redemption
                
                var res = await adal.AcquireTokenByAuthorizationCodeAsync(context.ProtocolMessage.Code, new Uri(redirect), new ClientCredential(_azureOptions.ClientId, _azureOptions.ClientSecret), "https://graph.windows.net");

                context.HandleCodeRedemption(res.AccessToken, res.IdToken);
            }

            public void Configure(OpenIdConnectOptions options)
            {
                Configure(Options.DefaultName, options);
            }
        }
    }
}
