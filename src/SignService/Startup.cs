using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignService.SigningTools;
using SignService.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using SignService.Services;
using Newtonsoft.Json;
using Microsoft.ApplicationInsights.AspNetCore;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.SnapshotCollector;
using Microsoft.AspNetCore.Mvc;

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
            // Configure SnapshotCollector from application settings
            services.Configure<SnapshotCollectorConfiguration>(Configuration.GetSection(nameof(SnapshotCollectorConfiguration)));

            // Add SnapshotCollector telemetry processor.
            services.AddSingleton<ITelemetryProcessorFactory>(sp => new SnapshotCollectorTelemetryProcessorFactory(sp));

            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            // Add framework services.
            services.AddAuthentication(sharedOptions =>
                                       {
                                           //  sharedOptions.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                                            //sharedOptions.DefaultAuthenticateScheme = OpenIdConnectDefaults.AuthenticationScheme;
                                           sharedOptions.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                                       })
                    .AddAzureAdBearer(options => Configuration.Bind("AzureAd", options))
                    .AddAzureAd(options => Configuration.Bind("AzureAd", options))
                    .AddCookie();

            services.AddSession();
            
            services.Configure<ResourceIds>(Configuration.GetSection("Resources"));
            services.Configure<AdminConfig>(Configuration.GetSection("Admin"));
            services.Configure<WindowsSdkFiles>(ConfigureWindowsSdkFiles);

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddSingleton<ITelemetryLogger, TelemetryLogger>();
            services.AddSingleton<IApplicationConfiguration, ApplicationConfiguration>();

            // Add in our User wrapper
            services.AddScoped<IUser, HttpContextUser>();

            // The Key Vault Service must be scoped as the context is per user in the request
            services.AddScoped<IKeyVaultService, KeyVaultService>();

            // Admin service contains per-user context
            services.AddScoped<IUserAdminService, UserAdminService>();
            services.AddScoped<IGraphHttpService, GraphHttpService>();
            services.AddScoped<IKeyVaultAdminService, KeyVaultAdminService>();

            // Code signing tools contain per-user/request data
            services.AddScoped<IAppxFileFactory, AppxFileFactory>();
            services.AddScoped<ICodeSignService, AzureSignToolSignService>();
            services.AddScoped<ICodeSignService, VsixSignService>();
            services.AddScoped<ICodeSignService, MageSignService>();
            services.AddScoped<ICodeSignService, AppInstallerService>();

            // If the NuGet signing feature flag is enabled, add the service
            if (bool.TryParse(Configuration["FeatureFlags:NuGetSignTool"], out var flagEnabled) && flagEnabled)
            {
                services.AddScoped<ICodeSignService, NuGetSignService>();
            }

            services.AddScoped<ISigningToolAggregate, SigningToolAggregate>();

            services.AddMvc()
                    .SetCompatibilityVersion(CompatibilityVersion.Version_2_1); ;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, 
                              IHostingEnvironment env, 
                              ILoggerFactory loggerFactory, 
                              IServiceProvider serviceProvider,
                              IApplicationConfiguration applicationConfiguration)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            loggerFactory.AddApplicationInsights(serviceProvider, LogLevel.Information);

            TelemetryConfiguration.Active.TelemetryInitializers.Add(new VersionTelemetry());
            
            // Retreive application specific config from Azure AD
            applicationConfiguration.InitializeAsync().Wait();

            Func<JsonSerializerSettings> jsonSettingsProvider = () =>
            {
                var settings = new JsonSerializerSettings
                {
                    ContractResolver = new CoreContractResolver(app.ApplicationServices),
                };
                return settings;
            };

            JsonConvert.DefaultSettings = jsonSettingsProvider;

            // This is here because we need to P/Invoke into clr.dll for _AxlPublicKeyBlobToPublicKeyToken 
            bool is64bit = IntPtr.Size == 8;
            var windir = Environment.GetEnvironmentVariable("windir");
            var fxDir = is64bit ? "Framework64" : "Framework";
            var netfxDir = $@"{windir}\Microsoft.NET\{fxDir}\v4.0.30319";
            AddEnvironmentPaths(new[] { netfxDir });

            
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseSession();

            app.UseAuthentication();

            app.UseMvc(routes =>
                       {
                           routes.MapRoute(
                               name: "default",
                               template: "{controller=Home}/{action=Index}/{id?}");
                       });
        }

        static void AddEnvironmentPaths(IEnumerable<string> paths)
        {
            var path = new[] { Environment.GetEnvironmentVariable("PATH") ?? string.Empty };
            string newPath = string.Join(Path.PathSeparator.ToString(), path.Concat(paths));
            Environment.SetEnvironmentVariable("PATH", newPath);
        }

        void ConfigureWindowsSdkFiles(WindowsSdkFiles options)
        {
            var contentPath = environment.ContentRootPath;

            // If we're running on Azure App Services, we have to invoke from the underlying
            // location due to CSRSS/registration-free COM manifest issues
            
            // running on azure
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REGION_NAME")))
            {
                var home = Environment.GetEnvironmentVariable("HOME_EXPANDED");
                if (!string.IsNullOrWhiteSpace(home))
                {
                    contentPath = $@"{home}\site\wwwroot";
                }
            }

            options.MakeAppxPath = Path.Combine(contentPath, "tools\\SDK\\makeappx.exe");
        }
        
        private class SnapshotCollectorTelemetryProcessorFactory : ITelemetryProcessorFactory
        {
            private readonly IServiceProvider _serviceProvider;

            public SnapshotCollectorTelemetryProcessorFactory(IServiceProvider serviceProvider) =>
                _serviceProvider = serviceProvider;

            public ITelemetryProcessor Create(ITelemetryProcessor next)
            {
                var snapshotConfigurationOptions = _serviceProvider.GetService<IOptions<SnapshotCollectorConfiguration>>();
                return new SnapshotCollectorTelemetryProcessor(next, configuration: snapshotConfigurationOptions.Value);
            }
        }

        private class VersionTelemetry : ITelemetryInitializer
        {
            public void Initialize(ITelemetry telemetry)
            {
                telemetry.Context.Component.Version = Program.AssemblyInformationalVersion;
            }
        }


    }
}
