using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignService.SigningTools;
using SignService.Utils;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace SignService
{
    public class Startup
    {
        readonly IHostingEnvironment environment;

        public Startup(IHostingEnvironment env, IConfiguration configuration)
        {
            environment = env;
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddAuthentication(sharedOptions =>
                                       {
                                           sharedOptions.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                                       })
                    .AddAzureAdBearer(options => Configuration.Bind("Authentication:AzureAd", options));


            services.Configure<Settings>(Configuration);
            // Path to the tools\sdk directory
            services.Configure<Settings>(s => s.WinSdkBinDirectory = Path.Combine(environment.ContentRootPath, @"tools\SDK"));

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddScoped<IKeyVaultService, KeyVaultService>();
            services.AddSingleton<IAppxFileFactory, AppxFileFactory>();
            services.AddSingleton<ICodeSignService, AzureSignToolCodeSignService>();
            services.AddSingleton<ICodeSignService, VsixSignService>();
            services.AddSingleton<ICodeSignService, MageSignService>();

            services.AddSingleton<ISigningToolAggregate, SigningToolAggregate>(sp => new SigningToolAggregate(sp.GetServices<ICodeSignService>().ToList(), sp.GetService<ILogger<SigningToolAggregate>>(), sp.GetService<IOptions<Settings>>()));

            services.AddMvc();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, IServiceProvider serviceProvider)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            
            // This is here because we need to P/Invoke into clr.dll for _AxlPublicKeyBlobToPublicKeyToken 
            bool is64bit = IntPtr.Size == 8;
            var windir = Environment.GetEnvironmentVariable("windir");
            var fxDir = is64bit ? "Framework64" : "Framework";
            var netfxDir = $@"{windir}\Microsoft.NET\{fxDir}\v4.0.30319";
            AddEnvironmentPaths(new[] { netfxDir });
            
            app.UseAuthentication();
            app.UseMvc();
        }

        static void AddEnvironmentPaths(IEnumerable<string> paths)
        {
            var path = new[] { Environment.GetEnvironmentVariable("PATH") ?? string.Empty };

            string newPath = string.Join(Path.PathSeparator.ToString(), path.Concat(paths));

            Environment.SetEnvironmentVariable("PATH", newPath);
        }
    }
}
