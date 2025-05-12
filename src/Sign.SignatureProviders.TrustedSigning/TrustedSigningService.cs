// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.CodeSigning;
using Microsoft.Extensions.Logging;
using Sign.Core;

namespace Sign.SignatureProviders.TrustedSigning
{
    internal sealed class TrustedSigningService : ISignatureAlgorithmProvider, ICertificateProvider, IDisposable
    {
        private readonly CertificateProfileClient _client;
        private readonly string _accountName;
        private readonly string _certificateProfileName;
        private readonly ILogger<TrustedSigningService> _logger;
        private readonly SemaphoreSlim _mutex = new(1);
        private X509Certificate2? _certificate;

        public TrustedSigningService(
            CertificateProfileClient certificateProfileClient,
            string accountName,
            string certificateProfileName,
            ILogger<TrustedSigningService> logger)
        {
            ArgumentNullException.ThrowIfNull(certificateProfileClient, paramName: nameof(certificateProfileClient));
            ArgumentException.ThrowIfNullOrEmpty(accountName, nameof(accountName));
            ArgumentException.ThrowIfNullOrEmpty(certificateProfileName, nameof(certificateProfileName));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _client = certificateProfileClient;
            _accountName = accountName;
            _certificateProfileName = certificateProfileName;
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
                return new X509Certificate2(_certificate);
            }

            await _mutex.WaitAsync(cancellationToken);

            try
            {
                if (_certificate is null)
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    _logger.LogTrace(Resources.FetchingCertificate);

                    Response<Stream> response = await _client.GetSignCertificateChainAsync(_accountName, _certificateProfileName, cancellationToken: cancellationToken);

                    using (response.Value)
                    {
                        byte[] rawData = new byte[response.Value.Length];
                        response.Value.Read(rawData, 0, rawData.Length);

                        X509Certificate2Collection collection = [];
                        collection.Import(rawData);

                        // This should contain the certificate chain in root->leaf order.
                        _certificate = collection[collection.Count - 1];

                        _logger.LogTrace(Resources.FetchedCertificate, stopwatch.Elapsed.TotalMilliseconds);
                        //print the certificate info
                        _logger.LogTrace($"{Resources.CertificateDetails}{Environment.NewLine}{_certificate.ToString(verbose: true)}");
                    }
                }
            }
            finally
            {
                _mutex.Release();
            }

            return new X509Certificate2(_certificate);
        }

        public async Task<RSA> GetRsaAsync(CancellationToken cancellationToken)
        {
            using X509Certificate2 certificate = await GetCertificateAsync(cancellationToken);
            RSA rsaPublicKey = certificate.GetRSAPublicKey()!;
            return new RSATrustedSigning(_client, _accountName, _certificateProfileName, rsaPublicKey);
        }
    }
}
