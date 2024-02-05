// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class VsixSignatureProvider : RetryingSignatureProvider, ISignatureProvider
    {
        private readonly ISignatureAlgorithmProvider _rsaProvider;
        private readonly ICertificateProvider _certificateService;
        private readonly IVsixSignTool _VsixSignTool;

        // Dependency injection requires a public constructor.
        public VsixSignatureProvider(
            ISignatureAlgorithmProvider rsaProvider,
            ICertificateProvider certificateManangerService,
            IVsixSignTool vsixSignTool,
            ILogger<ISignatureProvider> logger)
            : base(logger)
        {
            ArgumentNullException.ThrowIfNull(rsaProvider, nameof(rsaProvider));
            ArgumentNullException.ThrowIfNull(certificateManangerService, nameof(certificateManangerService));
            ArgumentNullException.ThrowIfNull(vsixSignTool, nameof(vsixSignTool));

            _certificateService = certificateManangerService;
            _rsaProvider = rsaProvider;
            _VsixSignTool = vsixSignTool;
        }

        public bool CanSign(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return string.Equals(file.Extension, ".vsix", StringComparison.OrdinalIgnoreCase);
        }

        public async Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            Logger.LogInformation(Resources.VsixSignatureProviderSigning, files.Count());

            if (_keyVaultService.IsInitialized())
            {
                using (X509Certificate2 certificate = await _keyVaultService.GetCertificateAsync())
                using (AsymmetricAlgorithm rsa = await _keyVaultService.GetRsaAsync())
                {
                    IEnumerable<Task<bool>> tasks = files.Select(file => SignAsync(args: null, file, rsa, certificate, options));

                    await Task.WhenAll(tasks);
                }
            }
            else if (_certificateService.IsInitialized())
            {
                using (X509Certificate2 certificate = await _certificateService.GetCertificateAsync())
                using (AsymmetricAlgorithm rsa = await _certificateService.GetRsaAsync())
                {
                    IEnumerable<Task<bool>> tasks = files.Select(file => SignAsync(args: null, file, rsa, certificate, options));

                    await Task.WhenAll(tasks);
                }
            }
            else
            {
                throw new InvalidOperationException(Resources.NoSignatureProvidersAvailableError);
            }
        }

        protected override async Task<bool> SignCoreAsync(string? args, FileInfo file, RSA rsaPrivateKey, X509Certificate2 certificate, SignOptions options)
        {
            // Dual isn't supported, use sha256
            SignConfigurationSet configuration = new(
                options.FileHashAlgorithm,
                options.FileHashAlgorithm,
                rsaPrivateKey,
                certificate);

            return await _VsixSignTool.SignAsync(file, configuration, options);
        }
    }
}