// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class VsixSigner : RetryingSigner, IDataFormatSigner
    {
        private readonly ICertificateProvider _certificateProvider;
        private readonly ISignatureAlgorithmProvider _signatureAlgorithmProvider;
        private readonly IVsixSignTool _vsixSignTool;

        // Dependency injection requires a public constructor.
        public VsixSigner(
            ISignatureAlgorithmProvider signatureAlgorithmProvider,
            ICertificateProvider certificateProvider,
            IVsixSignTool vsixSignTool,
            ILogger<IDataFormatSigner> logger)
            : base(logger)
        {
            ArgumentNullException.ThrowIfNull(signatureAlgorithmProvider, nameof(signatureAlgorithmProvider));
            ArgumentNullException.ThrowIfNull(certificateProvider, nameof(certificateProvider));
            ArgumentNullException.ThrowIfNull(vsixSignTool, nameof(vsixSignTool));

            _signatureAlgorithmProvider = signatureAlgorithmProvider;
            _certificateProvider = certificateProvider;
            _vsixSignTool = vsixSignTool;
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

            using (X509Certificate2 certificate = await _certificateProvider.GetCertificateAsync())
            using (RSA rsa = await _signatureAlgorithmProvider.GetRsaAsync())
            {
                IEnumerable<Task<bool>> tasks = files.Select(file => SignAsync(args: null, file, rsa, certificate, options));

                await Task.WhenAll(tasks);
            }
        }

        protected override async Task<bool> SignCoreAsync(string? args, FileInfo file, RSA rsaPrivateKey, X509Certificate2 certificate, SignOptions options)
        {
            // Dual isn't supported, use Sha256
            SignConfigurationSet configuration = new(
                options.FileHashAlgorithm,
                options.FileHashAlgorithm,
                rsaPrivateKey,
                certificate);

            return await _vsixSignTool.SignAsync(file, configuration, options);
        }
    }
}