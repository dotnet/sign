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
        Task<IEnumerable<GraphUser>> GetUsersAsync();
    }

    public class AdminService : IAdminService
    {
        readonly AdminConfig configuration;
        readonly AzureAdOptions azureAdOptions;
        readonly IHttpContextAccessor contextAccessor;
        readonly AuthenticationContext adalContext;
        readonly IGraphHttpService graphHttpService;

        public AdminService(IOptionsSnapshot<AdminConfig> configuration, IOptionsSnapshot<AzureAdOptions> azureAdOptions, IGraphHttpService graphHttpService)
        {
            this.configuration = configuration.Value;
            this.azureAdOptions = azureAdOptions.Value;
            this.graphHttpService = graphHttpService;
        }

        public async Task<IEnumerable<GraphUser>> GetUsersAsync()
        {
            var uri =$"/users?api-version=1.6";
            
            var result = await graphHttpService.Get<List<GraphUser>>(uri).ConfigureAwait(false);
            
            return result;
        }

    }
}
