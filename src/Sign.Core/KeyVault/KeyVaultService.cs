// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Certificates;
using Microsoft.Extensions.Logging;
using RSAKeyVaultProvider;

namespace Sign.Core
{
    internal sealed class KeyVaultService : IKeyVaultService
    {
        private Uri? _keyVaultUrl;
        private readonly ILogger<IKeyVaultService> _logger;
        private Task<KeyVaultCertificateWithPolicy>? _task;
        private TokenCredential? _tokenCredential;

        // Dependency injection requires a public constructor.
        public KeyVaultService(ILogger<IKeyVaultService> logger)
        {
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _logger = logger;
        }

        public async Task<X509Certificate2> GetCertificateAsync()
        {
            ThrowIfUninitialized();

            KeyVaultCertificateWithPolicy certificateWithPolicy = await _task!;

            return new X509Certificate2(certificateWithPolicy.Cer);
        }

        public async Task<RSA> GetRsaAsync()
        {
            ThrowIfUninitialized();

            KeyVaultCertificateWithPolicy certificateWithPolicy = await _task!;
            X509Certificate2 certificate = new(certificateWithPolicy.Cer);
            Uri keyIdentifier = certificateWithPolicy.KeyId;

            return RSAFactory.Create(_tokenCredential, keyIdentifier, certificate);
        }

        public void Initialize(Uri keyVaultUrl, TokenCredential tokenCredential, string certificateName)
        {
            ArgumentNullException.ThrowIfNull(keyVaultUrl, nameof(keyVaultUrl));
            ArgumentNullException.ThrowIfNull(tokenCredential, nameof(tokenCredential));
            ArgumentNullException.ThrowIfNull(certificateName, nameof(certificateName));

            if (string.IsNullOrEmpty(certificateName))
            {
                throw new ArgumentException(Resources.ValueCannotBeEmptyString, nameof(certificateName));
            }

            _keyVaultUrl = keyVaultUrl;
            _tokenCredential = tokenCredential;

            _task = GetKeyVaultCertificateAsync(keyVaultUrl, tokenCredential, certificateName);
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

        private void ThrowIfUninitialized()
        {
            if (_task is null)
            {
                throw new InvalidOperationException($"{nameof(Initialize)}(...) must be called first.");
            }
        }
    }
}