using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using SignService.Services;
using SignService.Utils;

namespace SignService.SigningTools
{
    public class NuGetSignService : ICodeSignService
    {
        readonly IKeyVaultService keyVaultService;
        readonly ILogger<NuGetSignService> logger;
        readonly ITelemetryLogger telemetryLogger;
        readonly string signtoolPath;
        readonly string signToolName;

        readonly ParallelOptions options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4
        };

        public NuGetSignService(IKeyVaultService keyVaultService, 
                                IHostingEnvironment hostingEnvironment, 
                                ILogger<NuGetSignService> logger,
                                ITelemetryLogger telemetryLogger)
        {
            this.keyVaultService = keyVaultService;
            this.logger = logger;
            this.telemetryLogger = telemetryLogger;
            signtoolPath = Path.Combine(hostingEnvironment.ContentRootPath, "tools\\NuGetKeyVaultSignTool\\NuGetKeyVaultSignTool.exe");
            signToolName = Path.GetFileName(signtoolPath);
        }
        public async Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files, string filter)
        {
            // Explicitly put this on a thread because Parallel.ForEach blocks
            await Task.Run(() => SubmitInternal(hashMode, name, description, descriptionUrl, files));
        }

        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new List<string>
        {
            ".nupkg",
            ".snupkg"
        };
        public bool IsDefault { get; }

        void SubmitInternal(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files)
        {
            logger.LogInformation("Signing NuGetKeyVaultSignTool job {0} with {1} files", name, files.Count());
            
            var keyVaultAccessToken = keyVaultService.GetAccessTokenAsync().Result;

            var args = $@"-f -tr {keyVaultService.CertificateInfo.TimestampUrl} -kvu {keyVaultService.CertificateInfo.KeyVaultUrl} -kvc {keyVaultService.CertificateInfo.CertificateName} -kva {keyVaultAccessToken}";
            
            Parallel.ForEach(files, options, (file, state) =>
            {
                telemetryLogger.OnSignFile(file, signToolName);
                var fileArgs = $@"sign ""{file}"" {args}";

                if (!Sign(fileArgs))
                {
                    throw new Exception($"Could not sign {file}");
                }
            });
        }

        // Inspired from https://github.com/squaredup/bettersigntool/blob/master/bettersigntool/bettersigntool/SignCommand.cs

        bool Sign(string args)
        {
            var retry = TimeSpan.FromSeconds(5);
            var attempt = 1;
            do
            {
                if (attempt > 1)
                {
                    logger.LogInformation($"Performing attempt #{attempt} of 3 attempts after {retry.TotalSeconds}s");
                    Thread.Sleep(retry);
                }

                if (RunSignTool(args))
                {
                    logger.LogInformation($"Signed successfully");
                    return true;
                }

                attempt++;

                retry = TimeSpan.FromSeconds(Math.Pow(retry.TotalSeconds, 1.5));

            } while (attempt <= 3);

            logger.LogError($"Failed to sign. Attempts exceeded");

            return false;
        }

        bool RunSignTool(string args)
        {
            // Append a sha256 signature
            using (var signtool = new Process
            {
                StartInfo =
                {
                    FileName = signtoolPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    Arguments = args
                }
            })
            {
                var startTime = DateTimeOffset.UtcNow; 
                var stopwatch = Stopwatch.StartNew();

                // redact args for log
                var redacted = args;
                if (args.Contains("-kva"))
                    redacted = args.Substring(0, args.IndexOf("-kva")) + "-kva *****";

                logger.LogInformation("Signing using {fileName}", signtool.StartInfo.FileName);
                signtool.Start();

                var output = signtool.StandardOutput.ReadToEnd();
                var error = signtool.StandardError.ReadToEnd();
                logger.LogInformation("Nupkg Out {NupkgOutput}", output);

                if(!string.IsNullOrWhiteSpace(error))
                    logger.LogInformation("Nupkg Err {NupkgError}", error);

                if (!signtool.WaitForExit(30 * 1000))
                {
                    logger.LogError("Error: NuGetKeyVaultSignTool took too long to respond {exitCode}", signtool.ExitCode);
                    try
                    {
                        signtool.Kill();
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("NuGetKeyVaultSignTool timed out and could not be killed", ex);
                    }

                    telemetryLogger.TrackDependency(signToolName, startTime, stopwatch.Elapsed, redacted, signtool.ExitCode);
                    logger.LogError("Error: NuGetKeyVaultSignTool took too long to respond {exitCode}", signtool.ExitCode);
                    throw new Exception($"NuGetKeyVaultSignTool took too long to respond");
                }

                telemetryLogger.TrackDependency(signToolName, startTime, stopwatch.Elapsed, redacted, signtool.ExitCode);

                if (signtool.ExitCode == 0)
                {
                    return true;
                }

                logger.LogError("Error: Signtool returned {exitCode}", signtool.ExitCode);

                return false;
            }

        }
    }
}
