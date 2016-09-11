using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Refit;
using SignServiceClient;

namespace SignClient
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Args
            // 0 config file
            // 1 sourceFile
            // 2 outputFile
            // 3 type: zip/file
            // 4 clientSecret
            // 5 name
            // 6 description
            // 7 descriptionUrl

            DoMain(args).Wait();
        }

        static async Task DoMain(string[] args)
        {

            var builder = new ConfigurationBuilder()
               .AddJsonFile(args[0])
               .AddEnvironmentVariables();

            var configuration = builder.Build();

            var settings = new RefitSettings
            {
                AuthorizationHeaderValueGetter = async () =>
                {
                    var context = new AuthenticationContext($"{configuration["Authentication:AzureAd:AADInstance"]}{configuration["Authentication:AzureAd:TenantId"]}");

                    var res = await context.AcquireTokenAsync(configuration["Service:ResourceId"],
                                                                    new ClientCredential(configuration["Authentication:AzureAd:ClientId"], args[4]));
                    return res.AccessToken;
                }
            };



            var client = RestService.For<ISignService>(configuration["Service:Url"], settings);

            var input = new FileInfo(args[1]);
            var output = new FileInfo(args[2]);
            Directory.CreateDirectory(output.DirectoryName);

        
                var mpContent = new MultipartFormDataContent("-----Boundary----");
                var content = new StreamContent(input.OpenRead());
                mpContent.Add(content, "source", input.Name);

            HttpResponseMessage response;
            if (args[3] == "file")
            {
                 response = await client.SignSingleFile(mpContent, args[5], args[6], args[7]);
            }
            else if (args[3] == "zip")
            {
                response = await client.SignZipFile(mpContent, args[5], args[6], args[7]);
            }
            else
            {
                throw new ArgumentException("type must be either zip or file");
            }

            var str = await response.Content.ReadAsStreamAsync();

                using (var fs = output.OpenWrite())
                {
                    await str.CopyToAsync(fs);
                }
            
        }
    }
}
