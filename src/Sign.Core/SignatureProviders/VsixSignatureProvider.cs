// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using OpenVsixSignTool.Core;

namespace Sign.Core
{
    internal sealed class VsixSignatureProvider : RetryingSignatureProvider, ISignatureProvider
    {
        private readonly IKeyVaultService _keyVaultService;
        private readonly IOpenVsixSignTool _openVsixSignTool;

        // Dependency injection requires a public constructor.
        public VsixSignatureProvider(
            IKeyVaultService keyVaultService,
            IOpenVsixSignTool openVsixSignTool,
            ILogger<ISignatureProvider> logger)
            : base(logger)
        {
            ArgumentNullException.ThrowIfNull(keyVaultService, nameof(keyVaultService));
            ArgumentNullException.ThrowIfNull(openVsixSignTool, nameof(openVsixSignTool));

            _keyVaultService = keyVaultService;
            _openVsixSignTool = openVsixSignTool;
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

            using (X509Certificate2 certificate = await _keyVaultService.GetCertificateAsync())
            using (RSA rsa = await _keyVaultService.GetRsaAsync())
            {
                IEnumerable<Task<bool>> tasks = files.Select(file => SignAsync(args: null, file, rsa, certificate, options));

                await Task.WhenAll(tasks);
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

            return await _openVsixSignTool.SignAsync(file, configuration, options);
        }
    }
}