using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureKeyVault;

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
                                                    // build the current config so we can get the key vault url
                                                    var built = builder.Build();

                                                    var endpoint = built["ConfigurationKeyVaultUrl"];
                                                    if (!string.IsNullOrWhiteSpace(endpoint))
                                                    {
                                                        var tokenProvider = new AzureServiceTokenProvider(azureAdInstance: built["AzureAd:AADInstance"]);
                                                        var kvClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback));
                                                        builder.AddAzureKeyVault(endpoint, kvClient, new DefaultKeyVaultSecretManager()); 
                                                    }
                                                }))
                   .UseStartup<Startup>()
                   .UseApplicationInsights()
                   .Build();
    }
}
