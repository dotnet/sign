using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;

namespace SignService
{
    public class Program
    {
        public static string AssemblyInformationalVersion => ThisAssembly.AssemblyInformationalVersion;
        public static void Main(string[] args)
        {
            BuildWebHost(args).Build().Run();
        }

        public static IWebHostBuilder BuildWebHost(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                    .UseApplicationInsights()
                    .ConfigureAppConfiguration((builder =>
                                                {
                                                    // Support optional App_Data location
                                                    builder.AddJsonFile(@"App_Data\appsettings.json", true, true);

                                                }))
                   .UseStartup<Startup>()
                   .ConfigureLogging(logging =>
                   {
                       logging.AddApplicationInsights();
                       logging.AddFilter<ApplicationInsightsLoggerProvider>("", LogLevel.Information);
                   });
    }
}
