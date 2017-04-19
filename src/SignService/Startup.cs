using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SignService.SigningTools;

namespace SignService
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

            if (env.IsDevelopment())
            {
                // For more details on using the user secret store see http://go.microsoft.com/fwlink/?LinkID=532709
                //builder.AddUserSecrets();
            }

            builder.AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.

            services.AddSingleton<ICodeSignService>(sp =>
                                                    {

                                                        var env = sp.GetService<IHostingEnvironment>();

                                                        return new SigntoolCodeSignService(
                                                            Configuration["CertificateInfo:TimeStampUrl"],
                                                            Configuration["CertificateInfo:Thumbprint"],
                                                            env.ContentRootPath, 
                                                            sp.GetService<ILogger<SigntoolCodeSignService>>());
                                                    });

            services.AddSingleton<ICodeSignService>(sp => new PowerShellCodeSignService(
                                                        Configuration["CertificateInfo:TimeStampUrl"],
                                                        Configuration["CertificateInfo:Thumbprint"],
                                                        sp.GetService<ILogger<PowerShellCodeSignService>>()));


            services.AddSingleton<ISigningToolAggregate, SigningToolAggregate>(sp => new SigningToolAggregate(sp.GetServices<ICodeSignService>().ToList()));

            services.AddMvc();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();
           

            app.UseJwtBearerAuthentication(new JwtBearerOptions
            {
                Authority = Configuration["Authentication:AzureAd:AADInstance"] + Configuration["Authentication:AzureAd:TenantId"],
                Audience = Configuration["Authentication:AzureAd:Audience"]
            });

            app.UseMvc();
        }
    }
}
