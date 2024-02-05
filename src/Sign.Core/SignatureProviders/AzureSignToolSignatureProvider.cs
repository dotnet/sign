// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
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

            _supportedFileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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

            _logger.LogInformation(Resources.AzureSignToolSignatureProviderSigning, files.Count());

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
                        string message = string.Format(CultureInfo.CurrentCulture, Resources.SigningFailed, file.FullName);

                        throw new Exception(message);
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
            const int maxAttempts = 3;
            var attempt = 1;

            do
            {
                if (attempt > 1)
                {
                    _logger.LogInformation(Resources.SigningAttempt, attempt, maxAttempts, retry.TotalSeconds);
                    await Task.Delay(retry);
                    retry = TimeSpan.FromSeconds(Math.Pow(retry.TotalSeconds, 1.5));
                }

                if (RunSignTool(signer, file, options))
                {
                    return true;
                }

                ++attempt;

            } while (attempt <= maxAttempts);

            _logger.LogError(Resources.SigningFailedAfterAllAttempts);

            return false;
        }

        private bool RunSignTool(AuthenticodeKeyVaultSigner signer, FileInfo file, SignOptions options)
        {
            FileInfo manifestFile = _toolConfigurationProvider.SignToolManifest;

            _logger.LogInformation(Resources.SigningFile, file.FullName);

            var success = false;
            var code = 0;
            const int S_OK = 0;

            try
            {
                using (var ctx = new Kernel32.ActivationContext(manifestFile))
                {
                    code = signer.SignFile(
                        file.FullName,
                        options.Description,
                        options.DescriptionUrl?.AbsoluteUri,
                        pageHashing: null,
                        _logger);
                    success = code == S_OK;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
            }

            if (success)
            {
                _logger.LogInformation(Resources.SigningSucceeded);
                return true;
            }

            _logger.LogError(Resources.SigningFailedWithError, code);

            return false;
        }
    }
}