using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AzureSign.Core;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class AzureSignToolSignatureProvider : IAzureSignToolSignatureProvider
    {
        private readonly IKeyVaultService _keyVaultService;
        private readonly ILogger _logger;
        private readonly HashSet<string> _supportedFileExtensions;
        private readonly IToolConfigurationProvider _toolConfigurationProvider;

        // Dependency injection requires a public constructor.
        public AzureSignToolSignatureProvider(
            IToolConfigurationProvider toolConfigurationProvider,
            IKeyVaultService keyVaultService,
            ILogger<ISignatureProvider> logger)
        {
            ArgumentNullException.ThrowIfNull(toolConfigurationProvider, nameof(toolConfigurationProvider));
            ArgumentNullException.ThrowIfNull(keyVaultService, nameof(keyVaultService));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _keyVaultService = keyVaultService;
            _logger = logger;
            _toolConfigurationProvider = toolConfigurationProvider;

            _supportedFileExtensions = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
            {
                ".appx",
                ".appxbundle",
                ".cab",
                ".cat",
                ".dll",
                ".eappx",
                ".eappxbundle",
                ".emsix",
                ".emsixbundle",
                ".exe",
                ".msi",
                ".msix",
                ".msixbundle",
                ".msm",
                ".msp",
                ".mst",
                ".ocx",
                ".ps1",
                ".psm1",
                ".stl",
                ".sys",
                ".vbs",
                ".vxd",
                ".winmd"
            };
        }

        public bool CanSign(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return _supportedFileExtensions.Contains(file.Extension);
        }

        public async Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            _logger.LogInformation("Signing SignTool job with {count} files", files.Count());

            TimeStampConfiguration timestampConfiguration;

            if (options.TimestampService is null)
            {
                timestampConfiguration = TimeStampConfiguration.None;
            }
            else
            {
                timestampConfiguration = new(options.TimestampService.AbsoluteUri, options.TimestampHashAlgorithm, TimeStampType.RFC3161);
            }

            using (X509Certificate2 certificate = await _keyVaultService.GetCertificateAsync())
            using (RSA rsa = await _keyVaultService.GetRsaAsync())
            using (AuthenticodeKeyVaultSigner signer = new(
                rsa,
                certificate,
                options.FileHashAlgorithm,
                timestampConfiguration))
            {
                // loop through all of the files here, looking for appx/eappx
                // mark each as being signed and strip appx
                await Parallel.ForEachAsync(files, async (file, state) =>
                {
                    if (!await SignAsync(signer, file, options))
                    {
                        throw new Exception($"Could not append sign {file}");
                    }
                });
            }
        }

        // Inspired from https://github.com/squaredup/bettersigntool/blob/master/bettersigntool/bettersigntool/SignCommand.cs
        private async Task<bool> SignAsync(
            AuthenticodeKeyVaultSigner signer,
            FileInfo file,
            SignOptions options)
        {
            TimeSpan retry = TimeSpan.FromSeconds(5);
            var attempt = 1;

            do
            {
                if (attempt > 1)
                {
                    _logger.LogInformation("Performing attempt #{attempt} of 3 attempts after {seconds}s", attempt, retry.TotalSeconds);
                    await Task.Delay(retry);
                    retry = TimeSpan.FromSeconds(Math.Pow(retry.TotalSeconds, 1.5));
                }

                if (RunSignTool(signer, file, options))
                {
                    return true;
                }

                ++attempt;

            } while (attempt <= 3);

            _logger.LogError("Failed to sign. Attempts exceeded.");

            return false;
        }

        private bool RunSignTool(AuthenticodeKeyVaultSigner signer, FileInfo file, SignOptions options)
        {
            FileInfo manifestFile = _toolConfigurationProvider.SignToolManifest;

            _logger.LogInformation("Signing using {fileName}", file.FullName);

            var success = false;
            var code = 0;
            const int S_OK = 0;

            try
            {
                using (var ctx = new Kernel32.ActivationContext(manifestFile))
                {
                    code = signer.SignFile(file.FullName, options.Description, options.DescriptionUrl?.AbsoluteUri, null, _logger);
                    success = code == S_OK;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
            }

            if (success)
            {
                _logger.LogInformation("Sign tool completed successfuly");
                return true;
            }

            _logger.LogError("Sign tool completed with error {errorCode}", code);

            return false;
        }
    }
}