using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using Refit;
using Wyam.Core.IO.Globbing;

namespace SignClient
{
    class SignCommand
    {
        readonly CommandLineApplication signCommandLineApplication;

        static class EXIT_CODES
        {
            public const int SUCCESS = 0;
            public const int INVALID_OPTIONS = 1;
            public const int FAILED = 2;
            public const int NO_INPUTS_FOUND = 3;
        }

        public SignCommand(CommandLineApplication signCommandLineApplication)
        {
            this.signCommandLineApplication = signCommandLineApplication;
        }

        public int Sign
        (
            CommandOption configFile,
            CommandOption inputFile,
            CommandOption baseDirectory,
            CommandOption outputFile,
            CommandOption fileList,
            CommandOption clientSecret,
            CommandOption username,
            CommandOption name,
            CommandOption description,
            CommandOption descriptionUrl,
            CommandOption maxConcurrency,
            CommandOption loggingLevel
        )
        {
            try
            {
                // verify required parameters
                if (!configFile.HasValue())
                {
                    signCommandLineApplication.Error.WriteLine("--config parameter is required");
                    return EXIT_CODES.INVALID_OPTIONS;
                }

                if (!inputFile.HasValue())
                {
                    signCommandLineApplication.Error.WriteLine("--input parameter is required");
                    return EXIT_CODES.INVALID_OPTIONS;
                }

                if (!name.HasValue())
                {
                    signCommandLineApplication.Error.WriteLine("--name parameter is required");
                    return EXIT_CODES.INVALID_OPTIONS;
                }

                if(!maxConcurrency.HasValue())
                {
                    maxConcurrency.Values.Add("4"); // default to 4
                }

                if(baseDirectory.HasValue())
                {
                    // Make sure this is rooted
                    if(!Path.IsPathRooted(baseDirectory.Value()))
                    {
                        signCommandLineApplication.Error.WriteLine("--directory parameter must be rooted if specified");
                        return EXIT_CODES.INVALID_OPTIONS;
                    }
                }

                if(!baseDirectory.HasValue())
                {
                    baseDirectory.Values.Add(Environment.CurrentDirectory);
                }

                var logLevel = LogLevel.Warning;

                if (loggingLevel.HasValue())
                {
                    if (!Enum.TryParse(typeof(LogLevel), loggingLevel.Value(), ignoreCase: true, out var logLevelObj))
                    {
                        signCommandLineApplication.Error.WriteLine("--logLevel parameter invalid. Valid options are: error, warning, info, verbose");
                        return EXIT_CODES.INVALID_OPTIONS;
                    }

                    logLevel = (LogLevel)logLevelObj;
                }

                void Log(string facility, LogLevel level, string message)
                {
                    if (level <= logLevel)
                    {
                        var writer = level == LogLevel.Error ? signCommandLineApplication.Error : signCommandLineApplication.Out;
                        writer.WriteLine($"[{facility}][{level}] {message}");
                    }
                }

                List<FileInfo> inputFiles;
                // If we're going to glob, we can't be fully rooted currently (fix me later)

                var isGlob = inputFile.Value().Contains('*');

                if (isGlob)
                {
                    if(Path.IsPathRooted(inputFile.Value()))
                    {
                        signCommandLineApplication.Error.WriteLine("--input parameter cannot be rooted when using a glob. Use a path relative to the working directory");
                        return EXIT_CODES.INVALID_OPTIONS;
                    }

                    inputFiles = Globber.GetFiles(new DirectoryInfo(baseDirectory.Value()), inputFile.Value())
                                        .ToList();
                }
                else
                {
                    inputFiles = new List<FileInfo>
                    {
                        new FileInfo(ExpandFilePath(inputFile.Value()))
                    };
                }

                                          

                var builder = new ConfigurationBuilder()
                              .AddJsonFile(ExpandFilePath(configFile.Value()))
                              .AddEnvironmentVariables();

                var configuration = builder.Build();


                Func<Task<string>> getAccessToken;


                var authority = $"{configuration["SignClient:AzureAd:AADInstance"]}{configuration["SignClient:AzureAd:TenantId"]}";

                var clientId = configuration["SignClient:AzureAd:ClientId"];
                var resourceId = configuration["SignClient:Service:ResourceId"];

                var logMsal = new LogCallback((LogLevel level, string message, bool containsPii) => Log("MSAL", level, message));

                // See if we have a Username option
                if (username.HasValue())
                {
                    // ROPC flow
                    var pca = PublicClientApplicationBuilder.Create(clientId)
                                                            .WithLogging(logMsal, logLevel, enablePiiLogging: false, enableDefaultPlatformLogging: true)
                                                            .WithAuthority(authority)
                                                            .Build();

                    var secret = new NetworkCredential("", clientSecret.Value()).SecurePassword;

                    getAccessToken = async () =>
                    {
                        Log("RESTCLIENT", LogLevel.Info, "Obtaining access token for PublicClientApplication.");

                        var tokenResult = await pca.AcquireTokenByUsernamePassword(new[] { $"{resourceId}/user_impersonation" }, username.Value(), secret).ExecuteAsync();

                        Log("RESTCLIENT", LogLevel.Info, $"Obtained access token for PublicClientApplication. Correlation ID = {tokenResult.CorrelationId}; Expires on = {tokenResult.ExpiresOn}.");

                        return tokenResult.AccessToken;
                    };
                }
                else
                {
                    var context = ConfidentialClientApplicationBuilder.Create(clientId)
                                                                      .WithLogging(logMsal, logLevel, enablePiiLogging: false, enableDefaultPlatformLogging: true)
                                                                      .WithAuthority(authority)
                                                                      .WithClientSecret(clientSecret.Value())
                                                                      .Build();

                    getAccessToken = async () =>
                    {
                        Log("RESTCLIENT", LogLevel.Info, "Obtaining access token for ConfidentialClientApplication.");

                        var tokenResult = await context.AcquireTokenForClient(new[] { $"{resourceId}/.default" }).ExecuteAsync();

                        Log("RESTCLIENT", LogLevel.Info, $"Obtained access token for PublicClientApplication. Correlation ID = {tokenResult.CorrelationId}; Expires on = {tokenResult.ExpiresOn}.");

                        return tokenResult.AccessToken;
                    };                    
                }

                // Setup Refit
                var settings = new RefitSettings
                {
                    AuthorizationHeaderValueGetter = getAccessToken
                };


                var client = RestService.For<ISignService>(configuration["SignClient:Service:Url"], settings);
                client.Client.Timeout = Timeout.InfiniteTimeSpan; // TODO: Make configurable on command line

                // var max concurrency
                if(!int.TryParse(maxConcurrency.Value(), out var maxC) || maxC < 1)
                {
                    signCommandLineApplication.Error.WriteLine("--maxConcurrency parameter is not valid");
                    return EXIT_CODES.INVALID_OPTIONS;
                }

                if (inputFiles.Count == 0)
                {
                    signCommandLineApplication.Error.WriteLine("No inputs found to sign.");
                    return EXIT_CODES.NO_INPUTS_FOUND;
                }

                Parallel.ForEach(inputFiles,new ParallelOptions { MaxDegreeOfParallelism = maxC } , input =>
                {
                    FileInfo output;

                    var sw = Stopwatch.StartNew();

                    // Special case if there's only one input file and the output has a value, treat it as a file
                    if(inputFiles.Count == 1 && outputFile.HasValue())
                    {
                        // See if it has a file extension and if not, treat as a directory and use the input file name
                        var outFileValue = outputFile.Value();
                        if(Path.HasExtension(outFileValue))
                        {
                            output = new FileInfo(ExpandFilePath(outputFile.Value()));
                        }
                        else
                        {
                            output = new FileInfo(Path.Combine(ExpandFilePath(outFileValue), inputFiles[0].Name));
                        }                        
                    }
                    else
                    {
                        // if the output is specified, treat it as a directory, if not, overwrite the current file
                        if(!outputFile.HasValue())
                        {
                            output = new FileInfo(input.FullName);
                        }
                        else
                        {
                            var relative = Path.GetRelativePath(baseDirectory.Value(), input.FullName);

                            var basePath = Path.IsPathRooted(outputFile.Value()) ?
                                           outputFile.Value() :
                                           $"{baseDirectory.Value()}{Path.DirectorySeparatorChar}{outputFile.Value()}";

                            var fullOutput = Path.Combine(basePath, relative);

                            output = new FileInfo(fullOutput);
                        }
                    }

                    // Ensure the output directory exists
                    Directory.CreateDirectory(output.DirectoryName);

                    // Do action

                    HttpResponseMessage response;

                    signCommandLineApplication.Out.WriteLine($"Submitting '{input.FullName}' for signing.");

                    response = client.SignFile(input,
                                                fileList.HasValue() ? new FileInfo(ExpandFilePath(fileList.Value())) : null,
                                                HashMode.Sha256,
                                                name.Value(),
                                                description.Value(),
                                                descriptionUrl.Value()).Result;

                    // Check response

                    if (!response.IsSuccessStatusCode)
                    {
                        signCommandLineApplication.Error.WriteLine($"Error signing '{input.FullName}'");
                        signCommandLineApplication.Error.WriteLine($"Server returned non Ok response: {(int)response.StatusCode} {response.ReasonPhrase}");
                        response.EnsureSuccessStatusCode(); // force the throw to break out of the loop
                    }

                    var str = response.Content.ReadAsStreamAsync().Result;

                    // If we're replacing the file, make sure to the existing one first
                    using var fs = new FileStream(output.FullName, FileMode.Create);
                    str.CopyTo(fs);

                    signCommandLineApplication.Out.WriteLine($"Successfully signed '{output.FullName}' in {sw.ElapsedMilliseconds} ms");
                });

                
            }
            catch (AuthenticationException e)
            {
                signCommandLineApplication.Error.WriteLine(e.Message);
                return EXIT_CODES.FAILED;
            }
            catch (Exception e)
            {
                signCommandLineApplication.Error.WriteLine("Exception: " + e);
                return EXIT_CODES.FAILED;
            }

            return EXIT_CODES.SUCCESS;

            string ExpandFilePath(string file)
            {
                if (!Path.IsPathRooted(file))
                {
                    return $"{baseDirectory.Value()}{Path.DirectorySeparatorChar}{file}";
                }
                return file;
            }
        }
    }
}
