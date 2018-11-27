using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenVsixSignTool.Core;
using SignService.Services;
using SignService.Utils;

namespace SignService.SigningTools
{
    public class VsixSignService : ICodeSignService
    {
        readonly IKeyVaultService keyVaultService;
        readonly ILogger<VsixSignService> logger;
        readonly ITelemetryLogger telemetryLogger;
        readonly string signToolName = nameof(VsixSignService);

        public VsixSignService(IKeyVaultService keyVaultService,
                               ILogger<VsixSignService> logger,
                               ITelemetryLogger telemetryLogger)
        {
            this.keyVaultService = keyVaultService;
            this.logger = logger;
            this.telemetryLogger = telemetryLogger;
        }
        public async Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files, string filter)
        {
            await SubmitInternal(hashMode, name, description, descriptionUrl, files);
        }

        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new List<string>
        {
            ".vsix"
        };

        public bool IsDefault { get; }

        async Task SubmitInternal(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files)
        {
            logger.LogInformation("Signing OpenVsixSignTool job {0} with {1} files", name, files.Count());

            // Dual isn't supported, use sha256
            var alg = hashMode == HashMode.Sha1 ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256;
            
            var config = new SignConfigurationSet
            (
                fileDigestAlgorithm: alg,
                signatureDigestAlgorithm: alg,
                publicCertificate: await keyVaultService.GetCertificateAsync(),
                signingKey: await keyVaultService.ToRSA()
            );

            try
            {
                var tasks = files.Select(file =>
                {
                    telemetryLogger.OnSignFile(file, signToolName);
                    return Sign(file, config, keyVaultService.CertificateInfo.TimestampUrl, alg);
                });

                await Task.WhenAll(tasks);
            }
            finally
            {
                config.SigningKey?.Dispose();
            }
        }

        // Inspired from https://github.com/squaredup/bettersigntool/blob/master/bettersigntool/bettersigntool/SignCommand.cs

        async Task<bool> Sign(string file, SignConfigurationSet config, string timestampUrl, HashAlgorithmName alg)
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

                if (await RunSignTool(file, config, timestampUrl, alg))
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

        async Task<bool> RunSignTool(string file, SignConfigurationSet config, string timestampUrl, HashAlgorithmName alg)
        {
            // Append a sha256 signature
            using (var package = OpcPackage.Open(file, OpcPackageFileMode.ReadWrite))
            {
                var startTime = DateTimeOffset.UtcNow;
                var stopwatch = Stopwatch.StartNew();


                logger.LogInformation("Signing {fileName}", file);


                var signBuilder = package.CreateSignatureBuilder();
                signBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();

                var signature = signBuilder.Sign(config);

                var failed = false;
                if (timestampUrl != null)
                {
                    var timestampBuilder = signature.CreateTimestampBuilder();
                    var result = await timestampBuilder.SignAsync(new Uri(timestampUrl), alg);
                    if (result == TimestampResult.Failed)
                    {
                        failed = true;
                        logger.LogError("Error timestamping VSIX");
                    }
                }

                telemetryLogger.TrackSignToolDependency(signToolName, file, startTime, stopwatch.Elapsed, null, failed ? 1 : 0);

                return !failed;
            }

        }
    }
}
