using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Refit;
using Newtonsoft.Json.Linq;

namespace SignClient
{
    public class Program
    {
        public static int Main(string[] args)
        {
            return DoMain(args)
                .Result;
        }

        static async Task<int> DoMain(string[] args)
        {
            try
            {
                // default args
                var desc = string.Empty;
                var descUrl = string.Empty;
                var iFile = string.Empty;
                var oFile = string.Empty;
                var fFile = string.Empty;
                var name = string.Empty;
                var configFile = string.Empty;
                var clientSecret = string.Empty;
                var userName = string.Empty;
                var hashMode = HashMode.Sha256;

                var command = Command.Sign;
                ArgumentSyntax.Parse(args, syntax =>
                {
                    syntax.DefineCommand("sign", ref command, Command.Sign, "Sign a file");
                    syntax.DefineOption("c|config", ref configFile, "Full path to config json file");
                    syntax.DefineOption("i|input", ref iFile, "Full path to input file");
                    syntax.DefineOption("o|output", ref oFile, "Full path to output file. May be same as input to overwrite");
                    syntax.DefineOption("h|hashmode", ref hashMode, s => (HashMode)Enum.Parse(typeof(HashMode), s, true), "Hash mode: either dual or Sha256. Default is dual, to sign with both Sha-1 and Sha-256 for files that support it. For files that don't support dual, Sha-256 is used");
                    syntax.DefineOption("f|filelist", ref fFile, "Full path to file containing paths of files to sign within an archive");
                    syntax.DefineOption("s|secret", ref clientSecret, "Client Secret");
                    syntax.DefineOption("r|user", ref userName, "Username");
                    syntax.DefineOption("n|name", ref name, "Name of project for tracking");
                    syntax.DefineOption("d|description", ref desc, "Description");
                    syntax.DefineOption("u|descriptionUrl", ref descUrl, "Description Url");
                });

                // verify required parameters
                if (string.IsNullOrWhiteSpace(configFile))
                {
                    Console.Error.WriteLine("-config parameter is required");
                    return -1;
                }

                if (string.IsNullOrWhiteSpace(iFile))
                {
                    Console.Error.WriteLine("-input parameter is required");
                    return -1;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    Console.Error.WriteLine("-name parameter is required");
                    return -1;
                }

                if (string.IsNullOrWhiteSpace(oFile))
                {
                    oFile = iFile;
                }

                var builder = new ConfigurationBuilder()
                    .AddJsonFile(configFile)
                    .AddEnvironmentVariables();

                var configuration = builder.Build();


                // Setup Refit
                var settings = new RefitSettings
                {
                    AuthorizationHeaderValueGetter = async () =>
                    {
                        var authority = $"{configuration["SignClient:AzureAd:AADInstance"]}{configuration["SignClient:AzureAd:TenantId"]}";
                        

                        var clientId = configuration["SignClient:AzureAd:ClientId"];
                        var resourceId = configuration["SignClient:Service:ResourceId"];

                        // See if we have a Username option
                        if (!string.IsNullOrWhiteSpace(userName))
                        {
                            // ROPC flow
                            // Cannot use ADAL since there's no support for ROPC in .NET Core
                            var parameters = new Dictionary<string, string>
                            {
                                {"resource", resourceId },
                                {"client_id", clientId },
                                {"grant_type", "password" },
                                {"username", userName },
                                {"password", clientSecret },
                            };
                            using (var adalClient = new HttpClient())
                            {
                                var result = await adalClient.PostAsync($"{authority}/oauth2/token", new FormUrlEncodedContent(parameters));
                                result.EnsureSuccessStatusCode();

                                var jObj = JObject.Parse(await result.Content.ReadAsStringAsync());
                                var token = jObj["access_token"].Value<string>();
                                return token;
                            }
                        }
                        else
                        {
                            // Client credential flow
                            var context = new AuthenticationContext(authority);
                            var res = await context.AcquireTokenAsync(resourceId, new ClientCredential(clientId, clientSecret));
                            return res.AccessToken;
                        }
                    }
                };


                var client = RestService.For<ISignService>(configuration["SignClient:Service:Url"], settings);

                // Prepare input/output file
                var input = new FileInfo(iFile);
                var output = new FileInfo(oFile);
                Directory.CreateDirectory(output.DirectoryName);


                // Do action
                
                HttpResponseMessage response;
                if (command == Command.Sign)
                {
                    response = await client.SignFile(input, !string.IsNullOrWhiteSpace(fFile) ? new FileInfo(fFile) : null, hashMode, name, desc, descUrl);
                }
                else
                {
                    throw new ArgumentException("type must be sign");
                }


                // Check response

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

        enum Command
        {
            Sign
        }
    }
}