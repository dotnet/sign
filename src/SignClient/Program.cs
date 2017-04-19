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
                var hashMode = HashMode.Sha256;

                var command = Command.File;
                ArgumentSyntax.Parse(args, syntax =>
                {
                    syntax.DefineCommand("file", ref command, Command.File, "Single file");
                    syntax.DefineOption("c|config", ref configFile, "Full path to config json file");
                    syntax.DefineOption("i|input", ref iFile, "Full path to input file");
                    syntax.DefineOption("o|output", ref oFile, "Full path to output file. May be same as input to overwrite. Defaults to input file if ommited");
                    syntax.DefineOption("h|hashmode", ref hashMode, s => (HashMode)Enum.Parse(typeof(HashMode), s, true), "Hash mode: either dual or Sha256. Default is Sha256. Dual signs with both Sha-1 and Sha-256 for files that support it. For files that don't support dual, Sha-256 is used");
                    syntax.DefineOption("s|secret", ref clientSecret, "Client Secret");
                    syntax.DefineOption("n|name", ref name, "Name of project for tracking");
                    syntax.DefineOption("d|description", ref desc, "Description");
                    syntax.DefineOption("u|descriptionUrl", ref descUrl, "Description Url");


                    syntax.DefineCommand("zip", ref command, Command.Zip, "Zip-type file (NuGet, etc)");
                    syntax.DefineOption("c|config", ref configFile, "Full path to config json file");
                    syntax.DefineOption("i|input", ref iFile, "Full path to input file");
                    syntax.DefineOption("o|output", ref oFile, "Full path to output file. May be same as input to overwrite");
                    syntax.DefineOption("h|hashmode", ref hashMode, s => (HashMode)Enum.Parse(typeof(HashMode), s, true), "Hash mode: either dual or Sha256. Default is dual, to sign with both Sha-1 and Sha-256 for files that support it. For files that don't support dual, Sha-256 is used");
                    syntax.DefineOption("f|filelist", ref fFile, "Full path to file containing paths of files to sign within an archive");
                    syntax.DefineOption("s|secret", ref clientSecret, "Client Secret");
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
                        var context = new AuthenticationContext($"{configuration["SignClient:AzureAd:AADInstance"]}{configuration["SignClient:AzureAd:TenantId"]}");

                        var res = await context.AcquireTokenAsync(configuration["SignClient:Service:ResourceId"],
                                                                new ClientCredential(configuration["SignClient:AzureAd:ClientId"],
                                                                                    clientSecret));
                        return res.AccessToken;
                    }
                };


                var client = RestService.For<ISignService>(configuration["SignClient:Service:Url"], settings);

                // Prepare input/output file
                var input = new FileInfo(iFile);
                var output = new FileInfo(oFile);
                Directory.CreateDirectory(output.DirectoryName);


                // Do action
                
                HttpResponseMessage response;
                if (command == Command.File)
                {
                    response = await client.SignSingleFile(input, hashMode, name, desc, descUrl);
                }
                else if (command == Command.Zip)
                {
                    response = await client.SignZipFile(input, !string.IsNullOrWhiteSpace(fFile) ? new FileInfo(fFile) : null, hashMode, name, desc, descUrl);
                }
                else
                {
                    throw new ArgumentException("type must be either zip or file");
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
            File,
            Zip
        }
    }
}