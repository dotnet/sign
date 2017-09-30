using System;
using System.Collections.Generic;
using System.Linq;
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
using SignService.Models;

namespace Microsoft.AspNetCore.Authentication
{
    public static class AzureAdServiceCollectionExtensions
    {
        //    public static AuthenticationBuilder AddAzureAdBearer(this AuthenticationBuilder builder)
        //        => builder.AddAzureAdBearer(_ => { });

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
            private readonly AzureAdOptions _azureOptions;
            private readonly Dictionary<string, CertificateInfo> _configuredUsers;

            public ConfigureAzureOptions(IOptions<AzureAdOptions> azureOptions, IOptions<Settings> settings)
            {
                _azureOptions = azureOptions.Value;
                _configuredUsers = settings.Value.UserCertificateInfoMap;
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

            Task OnTokenValidated(JwtBearer.TokenValidatedContext tokenValidatedContext)
            {
                var passed = false;

                var identity = (ClaimsIdentity)tokenValidatedContext.Principal.Identity;

                // See if there's a UPN, and if so, use that object id
                var upn = identity.Claims.FirstOrDefault(c => c.Type == "upn")?.Value;
                if (upn != null)
                {
                    var oid = identity.Claims.FirstOrDefault(c => c.Type == "oid")?.Value;
                    if (_configuredUsers.ContainsKey(oid))
                    {
                        passed = true;
                        identity.AddClaim(new Claim("authType", "user"));
                        identity.AddClaim(new Claim("authId", oid));
                    }
                }
                else // see if it's a supported application
                {
                    // see if it's an application id and if so, if present
                    var appid = identity.Claims.FirstOrDefault(c => c.Type == "appid")?.Value;
                    if (appid != null)
                    {
                        // see if it's configured
                        if (_configuredUsers.ContainsKey(appid))
                        {
                            passed = true;
                            identity.AddClaim(new Claim("authType", "application"));
                            identity.AddClaim(new Claim("authId", appid));
                        }
                    }
                }

                if (!passed)
                {
                    // If we get here, it's an unknown value
                    tokenValidatedContext.Fail("Unauthorized");
                }

                return Task.CompletedTask;
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
