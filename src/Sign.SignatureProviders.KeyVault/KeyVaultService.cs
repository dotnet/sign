// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RSAKeyVaultProvider;
using Sign.Core;

namespace Sign.SignatureProviders.KeyVault
{
    internal sealed class KeyVaultService : ISignatureAlgorithmProvider, ICertificateProvider
    {
        private readonly ILogger<KeyVaultService> _logger;
        private readonly Task<KeyVaultCertificateWithPolicy>? _task;
        private readonly TokenCredential _tokenCredential;

        internal KeyVaultService(
            IServiceProvider serviceProvider,
            TokenCredential tokenCredential,
            Uri keyVaultUrl,
            string certificateName)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));
            ArgumentNullException.ThrowIfNull(tokenCredential, nameof(tokenCredential));
            ArgumentNullException.ThrowIfNull(keyVaultUrl, nameof(keyVaultUrl));
            ArgumentException.ThrowIfNullOrEmpty(certificateName, nameof(certificateName));

            _tokenCredential = tokenCredential;
            _logger = serviceProvider.GetRequiredService<ILogger<KeyVaultService>>();

            _task = GetKeyVaultCertificateAsync(keyVaultUrl, tokenCredential, certificateName);
        }

        public async Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken)
        {
            KeyVaultCertificateWithPolicy certificateWithPolicy = await _task!;

            return new X509Certificate2(certificateWithPolicy.Cer);
        }

        public async Task<RSA> GetRsaAsync(CancellationToken cancellationToken)
        {
            KeyVaultCertificateWithPolicy certificateWithPolicy = await _task!;
            X509Certificate2 certificate = new(certificateWithPolicy.Cer);
            Uri keyIdentifier = certificateWithPolicy.KeyId;

            return RSAFactory.Create(_tokenCredential, keyIdentifier, certificate);
        }

        private async Task<KeyVaultCertificateWithPolicy> GetKeyVaultCertificateAsync(
            Uri keyVaultUrl,
            TokenCredential tokenCredential,
            string certificateName)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            _logger.LogTrace(Resources.FetchingCertificate);

            CertificateClient client = new(keyVaultUrl, tokenCredential);
            Response<KeyVaultCertificateWithPolicy>? response =
                await client.GetCertificateAsync(certificateName).ConfigureAwait(false);

            _logger.LogTrace(Resources.FetchedCertificate, stopwatch.Elapsed.TotalMilliseconds);

            return response.Value;
        }
    }
}