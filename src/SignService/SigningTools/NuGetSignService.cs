using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGetKeyVaultSignTool;
using SignService.Services;
using SignService.Utils;
using HashAlgorithmName = NuGet.Common.HashAlgorithmName;

namespace SignService.SigningTools
{
    public class NuGetSignService : ICodeSignService
    {
        readonly IKeyVaultService keyVaultService;
        readonly ILogger<NuGetSignService> logger;
        readonly ITelemetryLogger telemetryLogger;
        readonly string signToolName;
        readonly SignCommand signCommand;
        
        public NuGetSignService(IKeyVaultService keyVaultService,
                                ILogger<NuGetSignService> logger,
                                ITelemetryLogger telemetryLogger)
        {
            this.keyVaultService = keyVaultService;
            this.logger = logger;
            this.telemetryLogger = telemetryLogger;
            signToolName = nameof(NuGetSignService);
            signCommand = new SignCommand(logger);
        }
        public async Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files, string filter)
        {
            await SubmitInternal(hashMode, name, description, descriptionUrl, files);
        }

        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new List<string>
        {
            ".nupkg",
            ".snupkg"
        };
        public bool IsDefault { get; }

        async Task SubmitInternal(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files)
        {
            logger.LogInformation("Signing NuGetKeyVaultSignTool job {0} with {1} files", name, files.Count());
            
            var args = new SignArgs
            {
                HashAlgorithm = HashAlgorithmName.SHA256,
                TimestampUrl = keyVaultService.CertificateInfo.TimestampUrl,
                PublicCertificate = await keyVaultService.GetCertificateAsync(),
                Rsa = await keyVaultService.ToRSA()
            };

            try
            {
                var tasks = files.Select(file =>
                {
                    telemetryLogger.OnSignFile(file, signToolName);
                    return Sign(file, args);
                });

                await Task.WhenAll(tasks);
            }
            finally
            {
                args.Rsa?.Dispose();
            }
        }

        // Inspired from https://github.com/squaredup/bettersigntool/blob/master/bettersigntool/bettersigntool/SignCommand.cs

        async Task<bool> Sign(string file, SignArgs args)
        {
            var retry = TimeSpan.FromSeconds(5);
            var attempt = 1;
            do
            {
                if (attempt > 1)
                {
                    logger.LogInformation($"Performing attempt #{attempt} of 3 attempts after {retry.TotalSeconds}s");
                    await Task.Delay(retry);
                }

                if (await RunSignTool(file, args))
                {
                    logger.LogInformation($"Signed successfully");
                    return true;
                }

                attempt++;

                retry = TimeSpan.FromSeconds(Math.Pow(retry.TotalSeconds, 1.5));

            } while (attempt <= 3);

            logger.LogError($"Failed to sign. Attempts exceeded");

            throw new Exception($"Could not sign {file}");
        }

        async Task<bool> RunSignTool(string file, SignArgs args)
        {
            
            var startTime = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();


            logger.LogInformation("Signing using {fileName}", file);


            var success = false;
            try
            {
                 success = await signCommand.SignAsync(
                               file, 
                               file,
                               args.TimestampUrl,
                               args.HashAlgorithm,
                               args.HashAlgorithm,
                               true,
                               args.PublicCertificate,
                               args.Rsa
                              );
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
            }
            
            telemetryLogger.TrackSignToolDependency(signToolName, file, startTime, stopwatch.Elapsed, null, success ? 0 : -1);

            return success;
        }

#pragma warning disable IDE1006 // Naming Styles
        class SignArgs
        {
            public X509Certificate2 PublicCertificate { get; set; }

            public string TimestampUrl { get; set; }

            public HashAlgorithmName HashAlgorithm { get; set; }

            public RSA Rsa { get; set; }


        }
#pragma warning restore IDE1006 // Naming Styles
    }
}
