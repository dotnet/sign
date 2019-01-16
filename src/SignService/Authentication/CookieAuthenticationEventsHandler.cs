using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.AzureAD.UI;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using SignService.Models;

namespace SignService.Authentication
{
    public class CookieAuthenticationEventsHandler : CookieAuthenticationEvents
    {
        public override Task ValidatePrincipal(CookieValidatePrincipalContext context)
        {
            var contextAccessor = context.HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>();
            var azureOptions = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<AzureADOptions>>().Get(AzureADDefaults.AuthenticationScheme);

            var userId = context.Principal.FindFirst("oid").Value;

            // Check if exists in ADAL cache and reject if not. This happens if the cookie is alive and the server bounced
            var adal = new AuthenticationContext($"{azureOptions.Instance}{azureOptions.TenantId}", new ADALSessionCache(userId, contextAccessor));
            if (adal.TokenCache.Count == 0)
            {
                context.RejectPrincipal();
            }
            return Task.CompletedTask;
        }
    }
}
