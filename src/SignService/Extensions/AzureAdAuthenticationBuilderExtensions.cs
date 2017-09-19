using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SignService;

namespace Microsoft.AspNetCore.Authentication
{
    public static class AzureAdServiceCollectionExtensions
    {
        public static AuthenticationBuilder AddAzureAdBearer(this AuthenticationBuilder builder)
            => builder.AddAzureAdBearer(_ => { });

        public static AuthenticationBuilder AddAzureAdBearer(this AuthenticationBuilder builder, Action<AzureAdOptions> configureOptions)
        {
            builder.Services.Configure(configureOptions);
            builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureAzureOptions>();
            builder.AddJwtBearer();
            return builder;
        }

        private class ConfigureAzureOptions: IConfigureNamedOptions<JwtBearerOptions>
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
            }

            Task OnTokenValidated(TokenValidatedContext tokenValidatedContext)
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
    }
}
