using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore;
using Microsoft.Extensions.Configuration;

namespace SignService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BuildWebHost(args).Run();
        }

        public static IWebHost BuildWebHost(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                    .ConfigureAppConfiguration((builder =>
                                                {
                                                    // Look here as well since this location may be easier for VM deployments
                                                    builder.AddJsonFile(@"App_Data\appsettings.json", true, true);

                                                    // May just have the certificate mapping config
                                                    builder.AddJsonFile(@"App_Data\CertificateMapping.json", true, true);

                                                    // build to get current values so we can get key vault config
                                                    var built = builder.Build();

                                                    var keyVaultConfigUrl = built["ConfigurationVaultUrl"];
                                                    if (!string.IsNullOrWhiteSpace(keyVaultConfigUrl))
                                                    {
                                                        // Values may be in Key Vault
                                                        builder.AddAzureKeyVault(keyVaultConfigUrl, built["AzureAd:ClientId"], built["AzureAd:ClientSecret"]);
                                                    }
                                                }))
                   .UseStartup<Startup>()
                   .Build();
    }
}
