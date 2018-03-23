using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using SignService.SigningTools;
using SignService.Utils;
using SignService.Services;

namespace SignService
{
    public interface ICodeSignService
    {
        Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files, string filter);

        IReadOnlyCollection<string> SupportedFileExtensions { get; }

        bool IsDefault { get; }
    }

    class AzureSignToolCodeSignService : ICodeSignService
    {
        readonly ILogger<AzureSignToolCodeSignService> logger;
        readonly IAppxFileFactory appxFileFactory;
        readonly IKeyVaultService keyVaultService;
        readonly ITelemetryLogger telemetryLogger;
        readonly string keyVaultSignToolPath;
        readonly string signToolName;
        
        public AzureSignToolCodeSignService(ILogger<AzureSignToolCodeSignService> logger, 
                                            IAppxFileFactory appxFileFactory, 
                                            IKeyVaultService keyVaultService,
                                            IHostingEnvironment hostingEnvironment,
                                            ITelemetryLogger telemetryLogger)
        {
            this.logger = logger;
            this.appxFileFactory = appxFileFactory;
            this.keyVaultService = keyVaultService;
            this.telemetryLogger = telemetryLogger;
            keyVaultSignToolPath = Path.Combine(hostingEnvironment.ContentRootPath, "tools\\AzureSignTool\\AzureSignTool.exe");
            signToolName = Path.GetFileName(keyVaultSignToolPath);
        }

        public Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files, string filter)
        {
            // Explicitly put this on a thread because Parallel.ForEach blocks
            if (hashMode == HashMode.Sha1 || hashMode == HashMode.Dual)
                throw new ArgumentOutOfRangeException(nameof(hashMode), "Only Sha256 is supported");
            
            return Task.Run(() => SubmitInternal(hashMode, name, description, descriptionUrl, files));
        }

        void SubmitInternal(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files)
        {
            logger.LogInformation("Signing SignTool job {0} with {1} files", name, files.Count());
            
            var descArgsList = new List<string>();
            if (!string.IsNullOrWhiteSpace(description))
            {
                if (description.Contains("\""))
                    throw new ArgumentException(nameof(description));

                descArgsList.Add($@"-d ""{description}""");
            }
            if (!string.IsNullOrWhiteSpace(descriptionUrl))
            {
                if (descriptionUrl.Contains("\""))
                    throw new ArgumentException(nameof(descriptionUrl));

                descArgsList.Add($@"-du ""{descriptionUrl}""");
            }

            var descArgs = string.Join(" ", descArgsList);
            string keyVaultAccessToken = null;
            
            keyVaultAccessToken = keyVaultService.GetAccessTokenAsync().Result;

            // loop through all of the files here, looking for appx/eappx
            // mark each as being signed and strip appx
            Parallel.ForEach(files, (file, state) =>
            {
                telemetryLogger.OnSignFile(file, signToolName);

                // check to see if it's an appx and strip it first
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (".appx".Equals(ext, StringComparison.OrdinalIgnoreCase) || ".eappx".Equals(ext, StringComparison.OrdinalIgnoreCase))
                {
                    StripAppx(file);
                }

            });

            // generate a file list for signing
            using (var fileList = new TemporaryFile())
            {
                // generate a file of files
                File.WriteAllLines(fileList.FileName, files);

                var args = $@"sign -ifl ""{fileList.FileName}"" -v -tr {keyVaultService.CertificateInfo.TimestampUrl} -fd sha256 -td sha256 {descArgs} -kvu {keyVaultService.CertificateInfo.KeyVaultUrl} -kvc {keyVaultService.CertificateInfo.CertificateName} -kva {keyVaultAccessToken}";

                if (!Sign(args))
                {
                    throw new Exception($"Could not append sign one of \n{string.Join("\n", files)}");
                }
            }
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
                    return true;
                }

                attempt++;

                retry = TimeSpan.FromSeconds(Math.Pow(retry.TotalSeconds, 1.5));

            } while (attempt <= 3);

            logger.LogError($"Failed to sign. Attempts exceeded");

            return false;
        }

        void StripAppx(string appxFile)
        {
            // This will extract and resave the appx, stripping the signature
            // and fixing the publisher
            using (var appx = appxFileFactory.Create(appxFile))
            {
                appx.Save();
            }
        }

        bool RunSignTool(string args)
        {
            // Append a sha256 signature
            using (var signtool = new Process
            {
                StartInfo =
                {
                    FileName = keyVaultSignToolPath,
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

                logger.LogInformation(@"""{0}"" {1}", signtool.StartInfo.FileName, redacted);
                signtool.Start();
                var output = signtool.StandardOutput.ReadToEnd();
                var error = signtool.StandardError.ReadToEnd();
                logger.LogInformation("SignTool Out {SignToolOutput}", output);

                if(!string.IsNullOrWhiteSpace(error))
                    logger.LogError("SignTool Err {SignToolError}", error);



                if (!signtool.WaitForExit(30 * 1000))
                {
                    logger.LogError("Error: Signtool took too long to respond {0}", signtool.ExitCode);
                    try
                    {
                        signtool.Kill();
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("SignTool timed out and could not be killed", ex);
                    }

                    telemetryLogger.TrackDependency(signToolName, startTime, stopwatch.Elapsed, redacted, signtool.ExitCode);
                    logger.LogError("Error: Signtool took too long to respond {0}", signtool.ExitCode);
                    throw new Exception($"Sign tool took too long to respond with {redacted}");
                }

                telemetryLogger.TrackDependency(signToolName, startTime, stopwatch.Elapsed, redacted, signtool.ExitCode);

                if (signtool.ExitCode == 0)
                {
                    
                    logger.LogInformation("Sign tool completed successfuly");
                    return true;
                }

                logger.LogError("Error: Signtool returned {0}", signtool.ExitCode);

                return false;
            }
        }

        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new List<string>()
        {
            ".msi",
            ".msp",
            ".msm",
            ".mst",
            ".cab",
            ".cat",
            ".dll",
            ".exe",
            ".sys",
            ".vxd",
            ".winmd",
            ".appx",
            ".appxbundle",
            ".eappx",
            ".eappxbundle",
            ".ps1",
            ".psm1",
            ".vbs",
            ".ocx",
            ".stl"
            
        };

        public bool IsDefault => true;
    }
}
