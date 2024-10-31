// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Logging;
using Sign.Core;

namespace Sign.SignatureProviders.KeyVault
{
    internal sealed class KeyVaultService : ISignatureAlgorithmProvider, ICertificateProvider, IDisposable
    {
        private readonly TokenCredential _tokenCredential;
        private readonly Uri _keyVaultUrl;
        private readonly string _certificateName;
        private readonly ILogger<KeyVaultService> _logger;
        private readonly SemaphoreSlim _mutex = new(1);
        private KeyVaultCertificate? _certificate;
        private readonly string? _certificateVersion;

        internal KeyVaultService(
            TokenCredential tokenCredential,
            Uri keyVaultUrl,
            string certificateName,
            string? certificateVersion,
            ILogger<KeyVaultService> logger)
        {
            ArgumentNullException.ThrowIfNull(tokenCredential, nameof(tokenCredential));
            ArgumentNullException.ThrowIfNull(keyVaultUrl, nameof(keyVaultUrl));
            ArgumentException.ThrowIfNullOrEmpty(certificateName, nameof(certificateName));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _tokenCredential = tokenCredential;
            _keyVaultUrl = keyVaultUrl;
            _certificateName = certificateName;
            _certificateVersion = certificateVersion;
            _logger = logger;
        }

        public void Dispose()
        {
            _mutex.Dispose();
            GC.SuppressFinalize(this);
        }

        public async Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken)
        {
            KeyVaultCertificate certificateWithPolicy = await GetCertificateVersionAsync(cancellationToken);

            return new X509Certificate2(certificateWithPolicy.Cer);
        }

        public async Task<RSA> GetRsaAsync(CancellationToken cancellationToken)
        {
            KeyVaultCertificate certificateWithPolicy = await GetCertificateVersionAsync(cancellationToken);

            CryptographyClient cryptoClient = new(certificateWithPolicy.KeyId, _tokenCredential);
            return await cryptoClient.CreateRSAAsync(cancellationToken);
        }

        private async Task<KeyVaultCertificate> GetCertificateVersionAsync(CancellationToken cancellationToken)
        {
            if (_certificate is not null)
            {
                return _certificate;
            }

            await _mutex.WaitAsync(cancellationToken);

            try
            {
                if (_certificate is null)
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    _logger.LogTrace(Resources.FetchingCertificate);

                    CertificateClient client = new(_keyVaultUrl, _tokenCredential);
                    if(_certificateVersion is null)
                    {
                        Response<KeyVaultCertificateWithPolicy> response = await client.GetCertificateAsync(_certificateName, cancellationToken);
                        _certificate = response.Value;
                    }
                    else
                    {
                        Response<KeyVaultCertificate> response =
                            await client.GetCertificateVersionAsync(_certificateName, _certificateVersion,
                                cancellationToken);
                        _certificate = response.Value;
                    }

                    _logger.LogTrace(Resources.FetchedCertificate, stopwatch.Elapsed.TotalMilliseconds);


                }
            }
            finally
            {
                _mutex.Release();
            }

            return _certificate;
        }
    }
}
