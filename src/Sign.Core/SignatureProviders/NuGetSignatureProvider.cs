// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class NuGetSignatureProvider : RetryingSignatureProvider, ISignatureProvider
    {
        private readonly IKeyVaultService _keyVaultService;
        private readonly INuGetSignTool _nuGetSignTool;

        // Dependency injection requires a public constructor.
        public NuGetSignatureProvider(
            IKeyVaultService keyVaultService,
            INuGetSignTool nuGetSignTool,
            ILogger<ISignatureProvider> logger)
            : base(logger)
        {
            ArgumentNullException.ThrowIfNull(keyVaultService, nameof(keyVaultService));
            ArgumentNullException.ThrowIfNull(nuGetSignTool, nameof(nuGetSignTool));

            _keyVaultService = keyVaultService;
            _nuGetSignTool = nuGetSignTool;
        }

        public bool CanSign(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return string.Equals(file.Extension, ".nupkg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.Extension, ".snupkg", StringComparison.OrdinalIgnoreCase);
        }

        public async Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            using (X509Certificate2 certificate = await _keyVaultService.GetCertificateAsync())
            using (RSA rsa = await _keyVaultService.GetRsaAsync())
            {
                IEnumerable<Task<bool>> tasks = files.Select(file => SignAsync(args: null, file, rsa, certificate, options));

                await Task.WhenAll(tasks);
            }
        }

        protected override Task<bool> SignCoreAsync(string? args, FileInfo file, RSA rsaPrivateKey, X509Certificate2 certificate, SignOptions options)
        {
            return _nuGetSignTool.SignAsync(file, rsaPrivateKey, certificate, options);
        }
    }
}