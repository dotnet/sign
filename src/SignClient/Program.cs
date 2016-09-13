using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Refit;
using SignServiceClient;

namespace SignClient
{
    public class Program
    {
        public static int Main(string[] args)
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

            return DoMain(args)
                .Result;
        }

        static async Task<int> DoMain(string[] args)
        {
            try
            {
                var builder = new ConfigurationBuilder()
                    .AddJsonFile(args[0])
                    .AddEnvironmentVariables();

                var configuration = builder.Build();

                var settings = new RefitSettings
                {
                    AuthorizationHeaderValueGetter = async () =>
                                                     {
                                                         var context = new AuthenticationContext($"{configuration["SignClient:AzureAd:AADInstance"]}{configuration["SignClient:AzureAd:TenantId"]}");

                                                         var res = await context.AcquireTokenAsync(configuration["SignClient:Service:ResourceId"],
                                                                                                   new ClientCredential(configuration["SignClient:AzureAd:ClientId"], args[4]));
                                                         return res.AccessToken;
                                                     }
                };


                var client = RestService.For<ISignService>(configuration["SignClient:Service:Url"], settings);

                var input = new FileInfo(args[1]);
                var output = new FileInfo(args[2]);
                Directory.CreateDirectory(output.DirectoryName);


                var mpContent = new MultipartFormDataContent("-----Boundary----");
                var content = new StreamContent(input.OpenRead());
                mpContent.Add(content, "source", input.Name);

                var desc = string.Empty;
                var descUrl = string.Empty;

                if (args.Length >= 7)
                {
                    desc = args[6];
                }
                if (args.Length >= 8)
                {
                    descUrl = args[7];
                }

                HttpResponseMessage response;
                if (args[3] == "file")
                {
                    response = await client.SignSingleFile(mpContent, args[5], desc, descUrl);
                }
                else if (args[3] == "zip")
                {
                    response = await client.SignZipFile(mpContent, args[5], desc, descUrl);
                }
                else
                {
                    throw new ArgumentException("type must be either zip or file");
                }

                if (!response.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"Server returned non Ok response: {(int)response.StatusCode} {response.ReasonPhrase}");
                    return -1;
                }

                var str = await response.Content.ReadAsStreamAsync();

                using (var fs = output.OpenWrite())
                {
                    await str.CopyToAsync(fs);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Exception:" + e);

                return -1;
            }

            return 0;
        }
    }
}