// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Azure;
using Azure.CodeSigning;
using Azure.CodeSigning.Models;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Sign.Core;

namespace Sign.SignatureProviders.TrustedSigning
{
    internal sealed class TrustedSigningService : ISignatureAlgorithmProvider, ICertificateProvider, IDisposable
    {
        private static readonly SignRequest _emptyRequest = new(SignatureAlgorithm.RS256, new byte[32]);

        private readonly CertificateProfileClient _client;
        private readonly string _accountName;
        private readonly string _certificateProfileName;
        private readonly ILogger<TrustedSigningService> _logger;
        private readonly SemaphoreSlim _mutex = new(1);
        private X509Certificate2? _publicKey;

        public TrustedSigningService(
            TokenCredential tokenCredential,
            Uri endpointUrl,
            string accountName,
            string certificateProfileName,
            ILogger<TrustedSigningService> logger)
        {
            ArgumentNullException.ThrowIfNull(tokenCredential, nameof(tokenCredential));
            ArgumentNullException.ThrowIfNull(endpointUrl, nameof(endpointUrl));
            ArgumentException.ThrowIfNullOrEmpty(accountName, nameof(accountName));
            ArgumentException.ThrowIfNullOrEmpty(certificateProfileName, nameof(certificateProfileName));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _accountName = accountName;
            _certificateProfileName = certificateProfileName;
            _logger = logger;

            _client = new CertificateProfileClient(tokenCredential, endpointUrl);
        }

        public void Dispose()
        {
            _mutex.Dispose();
            _publicKey?.Dispose();
            GC.SuppressFinalize(this);
        }

        public async Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken)
        {
            if (_publicKey is not null)
            {
                return new X509Certificate2(_publicKey);
            }

            await _mutex.WaitAsync(cancellationToken);
            try
            {
                if (_publicKey is null)
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    _logger.LogTrace(Resources.FetchingCertificate);

                    CertificateProfileSignOperation operation = await _client.StartSignAsync(_accountName, _certificateProfileName, _emptyRequest, cancellationToken: cancellationToken);
                    Response<SignStatus> response = await operation.WaitForCompletionAsync(cancellationToken);

                    byte[] rawData = Convert.FromBase64String(Encoding.UTF8.GetString(response.Value.SigningCertificate));
                    X509Certificate2Collection collection = [];
                    collection.Import(rawData);

                    _publicKey = collection[collection.Count - 1];

                    _logger.LogTrace(Resources.FetchedCertificate, stopwatch.Elapsed.TotalMilliseconds);
                }
            }
            finally
            {
                _mutex.Release();
            }

            return new X509Certificate2(_publicKey);
        }

        public async Task<RSA> GetRsaAsync(CancellationToken cancellationToken)
        {
            X509Certificate2 publicKey = await GetCertificateAsync(cancellationToken);
            return new RSATrustedSigning(_client, _accountName, _certificateProfileName, publicKey);
        }
    }
}
