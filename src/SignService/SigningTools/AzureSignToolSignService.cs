using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AzureSign.Core;
using Microsoft.Extensions.Logging;
using SignService.Services;
using SignService.SigningTools;
using SignService.Utils;

namespace SignService
{

    class AzureSignToolSignService : ICodeSignService
    {
        readonly ILogger<AzureSignToolSignService> logger;
        readonly IKeyVaultService keyVaultService;
        readonly ITelemetryLogger telemetryLogger;
        readonly string signToolName;

        public AzureSignToolSignService(ILogger<AzureSignToolSignService> logger,
                                            IKeyVaultService keyVaultService,
                                            ITelemetryLogger telemetryLogger)
        {
            this.logger = logger;
            this.keyVaultService = keyVaultService;
            this.telemetryLogger = telemetryLogger;
            signToolName = nameof(AzureSignToolSignService);
        }

        public Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files, string filter)
        {
            // Explicitly put this on a thread because Parallel.ForEach blocks
            if (hashMode == HashMode.Sha1 || hashMode == HashMode.Dual)
            {
                throw new ArgumentOutOfRangeException(nameof(hashMode), "Only Sha256 is supported");
            }

            return Task.Run(() => SubmitInternal(hashMode, name, description, descriptionUrl, files));
        }

        void SubmitInternal(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files)
        {
            logger.LogInformation("Signing SignTool job {0} with {1} files", name, files.Count());
            
            var certificate = keyVaultService.GetCertificateAsync().Result;
            using (var rsa = keyVaultService.ToRSA().Result)
            using (var signer = new AuthenticodeKeyVaultSigner(rsa, certificate, HashAlgorithmName.SHA256, new TimeStampConfiguration(keyVaultService.CertificateInfo.TimestampUrl, HashAlgorithmName.SHA256, TimeStampType.RFC3161)))
            {
                // loop through all of the files here, looking for appx/eappx
                // mark each as being signed and strip appx
                Parallel.ForEach(files, (file, state) =>
                {
                    telemetryLogger.OnSignFile(file, signToolName);

                    if (!Sign(signer, file, description, descriptionUrl))
                    {
                        throw new Exception($"Could not append sign {file}");
                    }
                });
            }
        }

        // Inspired from https://github.com/squaredup/bettersigntool/blob/master/bettersigntool/bettersigntool/SignCommand.cs

        bool Sign(AuthenticodeKeyVaultSigner signer, string file, string description, string descriptionUrl)
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

                if (RunSignTool(signer, file, description, descriptionUrl))
                {
                    return true;
                }

                attempt++;

                retry = TimeSpan.FromSeconds(Math.Pow(retry.TotalSeconds, 1.5));

            } while (attempt <= 3);

            logger.LogError($"Failed to sign. Attempts exceeded");

            return false;
        }

        bool RunSignTool(AuthenticodeKeyVaultSigner signer, string file, string description, string descriptionUrl)
        {
            var startTime = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();

            logger.LogInformation("Signing using {fileName}", file);

            var success = false;
            var code = 0;
            try
            {
                using (var ctx = new Kernel32.ActivationContext(Startup.ManifestLocation))
                {
                    code = signer.SignFile(file, description, descriptionUrl, null);
                    success = code == 0;
                }

                telemetryLogger.TrackSignToolDependency(signToolName, file, startTime, stopwatch.Elapsed, null, code);
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
            }
            
            if (success)
            {

                logger.LogInformation("Sign tool completed successfuly");
                return true;
            }

            logger.LogError("Sign tool completed with error {errorCode}", code);

            return false;
            
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
            ".msix",
            ".msixbundle",
            ".emsix",
            ".emsixbundle",
            ".ps1",
            ".psm1",
            ".vbs",
            ".ocx",
            ".stl"

        };

        public bool IsDefault => true;
    }
}
