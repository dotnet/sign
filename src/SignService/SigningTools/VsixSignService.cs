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
    public class VsixSignService : ICodeSignService
    {
        readonly IKeyVaultService keyVaultService;
        readonly ILogger<VsixSignService> logger;
        readonly ITelemetryLogger telemetryLogger;
        readonly string signtoolPath;
        readonly string signToolName;

        readonly ParallelOptions options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4
        };

        public VsixSignService(IKeyVaultService keyVaultService, 
                               IHostingEnvironment hostingEnvironment, 
                               ILogger<VsixSignService> logger,
                               ITelemetryLogger telemetryLogger)
        {
            this.keyVaultService = keyVaultService;
            this.logger = logger;
            this.telemetryLogger = telemetryLogger;
            signtoolPath = Path.Combine(hostingEnvironment.ContentRootPath, "tools\\OpenVsixSignTool\\OpenVsixSignTool.exe");
            signToolName = Path.GetFileName(signtoolPath);
        }
        public async Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files, string filter)
        {
            // Explicitly put this on a thread because Parallel.ForEach blocks
            await Task.Run(() => SubmitInternal(hashMode, name, description, descriptionUrl, files));
        }

        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new List<string>
        {
            ".vsix"
        };
        public bool IsDefault { get; }

        void SubmitInternal(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files)
        {
            logger.LogInformation("Signing OpenVsixSignTool job {0} with {1} files", name, files.Count());

            // Dual isn't supported, use sha256
            var alg = hashMode == HashMode.Sha1 ? "sha1" : "sha256";
            
            var keyVaultAccessToken = keyVaultService.GetAccessTokenAsync().Result;

            var args = $@"sign --timestamp {keyVaultService.CertificateInfo.TimestampUrl} -ta {alg} -fd {alg} -kvu {keyVaultService.CertificateInfo.KeyVaultUrl} -kvc {keyVaultService.CertificateInfo.CertificateName} -kva {keyVaultAccessToken}";
            

            Parallel.ForEach(files, options, (file, state) =>
                                             {
                                                 telemetryLogger.OnSignFile(file, signToolName);
                                                 var fileArgs = $@"{args} ""{file}""";

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

                logger.LogInformation("Signing {fileName}", signtool.StartInfo.FileName);
                signtool.Start();

                var output = signtool.StandardOutput.ReadToEnd();
                var error = signtool.StandardError.ReadToEnd();
                logger.LogInformation("Vsix Out {VsixOutput}", output);

                if(!string.IsNullOrWhiteSpace(error))
                    logger.LogInformation("Vsix Err {VsixError}", error);

                if (!signtool.WaitForExit(30 * 1000))
                {
                    logger.LogError("Error: OpenVsixSignTool took too long to respond {exitCode}", signtool.ExitCode);
                    try
                    {
                        signtool.Kill();
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("OpenVsixSignTool timed out and could not be killed", ex);
                    }

                    telemetryLogger.TrackDependency(signToolName, startTime, stopwatch.Elapsed, redacted, signtool.ExitCode);
                    logger.LogError("Error: OpenVsixSignTool took too long to respond {exitCode}", signtool.ExitCode);
                    throw new Exception($"OpenVsixSignTool took too long to respond");
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
