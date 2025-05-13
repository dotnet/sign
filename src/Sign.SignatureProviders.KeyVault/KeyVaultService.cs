// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Logging;
using Sign.Core;

namespace Sign.SignatureProviders.KeyVault
{
    internal sealed class KeyVaultService : ISignatureAlgorithmProvider, ICertificateProvider, IDisposable
    {
        private readonly CertificateClient _certificateClient;
        private readonly CryptographyClient _cryptographyClient;
        private readonly string _certificateName;
        private readonly ILogger<KeyVaultService> _logger;
        private readonly SemaphoreSlim _mutex = new(1);
        private X509Certificate2? _certificate;

        internal KeyVaultService(
            CertificateClient certificateClient,
            CryptographyClient cryptographyClient,
            string certificateName,
            ILogger<KeyVaultService> logger)
        {
            ArgumentNullException.ThrowIfNull(certificateClient, nameof(certificateClient));
            ArgumentNullException.ThrowIfNull(cryptographyClient, nameof(cryptographyClient));
            ArgumentException.ThrowIfNullOrEmpty(certificateName, nameof(certificateName));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _certificateName = certificateName;
            _certificateClient = certificateClient;
            _cryptographyClient = cryptographyClient;
            _logger = logger;
        }

        public void Dispose()
        {
            _mutex.Dispose();
            _certificate?.Dispose();
            GC.SuppressFinalize(this);
        }

        public async Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken)
        {
            if (_certificate is not null)
            {
                return new X509Certificate2(_certificate); // clone it as it's disposable
            }

            await _mutex.WaitAsync(cancellationToken);

            try
            {
                if (_certificate is null)
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    _logger.LogTrace(Resources.FetchingCertificate);

                    Response<KeyVaultCertificateWithPolicy> response = await _certificateClient.GetCertificateAsync(_certificateName, cancellationToken);

                    _logger.LogTrace(Resources.FetchedCertificate, stopwatch.Elapsed.TotalMilliseconds);

                    _certificate = new X509Certificate2(response.Value.Cer);

                    //print the certificate info
                    _logger.LogTrace($"{Resources.CertificateDetails}{Environment.NewLine}{_certificate.ToString(verbose: true)}");
                }
            }
            finally
            {
                _mutex.Release();
            }

            return new X509Certificate2(_certificate); // clone it as it's disposable
        }

        public async Task<RSA> GetRsaAsync(CancellationToken cancellationToken)
        {
            using X509Certificate2 certificate = await GetCertificateAsync(cancellationToken);
            RSAKeyVault rsaKeyVault = await _cryptographyClient.CreateRSAAsync(cancellationToken);
            RSA rsaPublicKey = certificate.GetRSAPublicKey()!;
            return new RSAKeyVaultWrapper(rsaKeyVault, rsaPublicKey);
        }
    }
}
