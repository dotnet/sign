// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Logging;
using Sign.Core;

namespace Sign.SignatureProviders.KeyVault
{
    internal sealed class KeyVaultService : ISignatureAlgorithmProvider, ICertificateProvider, IDisposable
    {
        private readonly CertificateClient _certificateClient;
        private readonly string? _certificateVersion;
        private readonly string _certificateName;
        private readonly KeyClient _keyClient;
        private readonly ILogger<KeyVaultService> _logger;
        private readonly SemaphoreSlim _mutex = new(1);
        private CertificateInfo? _certificateInfo;
        private CryptographyClient? _cryptographyClient;

        internal KeyVaultService(
            CertificateClient certificateClient,
            KeyClient keyClient,
            string certificateName,
            string? certificateVersion,
            ILogger<KeyVaultService> logger)
        {
            ArgumentNullException.ThrowIfNull(certificateClient, nameof(certificateClient));
            ArgumentNullException.ThrowIfNull(keyClient, nameof(keyClient));
            ArgumentException.ThrowIfNullOrEmpty(certificateName, nameof(certificateName));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _certificateName = certificateName;
            _certificateVersion = certificateVersion;
            _certificateClient = certificateClient;
            _keyClient = keyClient;
            _logger = logger;
        }

        public void Dispose()
        {
            _mutex.Dispose();
            _certificateInfo?.Certificate.Dispose();
            GC.SuppressFinalize(this);
        }

        public async Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken)
        {
            CertificateInfo certificateInfo = await GetCertificateInfoAsync(cancellationToken);

            return new X509Certificate2(certificateInfo.Certificate); // clone it as it's disposable
        }

        public async Task<RSA> GetRsaAsync(CancellationToken cancellationToken)
        {
            CertificateInfo certificateInfo = await GetCertificateInfoAsync(cancellationToken);

            await _mutex.WaitAsync(cancellationToken);

            try
            {
                _cryptographyClient ??= _keyClient.GetCryptographyClient(
                    certificateInfo.KeyIdentifier.Name,
                    certificateInfo.KeyIdentifier.Version);
            }
            finally
            {
                _mutex.Release();
            }

            RSAKeyVault rsaKeyVault = await _cryptographyClient.CreateRSAAsync(cancellationToken);
            RSA rsaPublicKey = certificateInfo.Certificate.GetRSAPublicKey()!;
            return new RSAKeyVaultWrapper(rsaKeyVault, rsaPublicKey);
        }

        private async Task<CertificateInfo> GetCertificateInfoAsync(CancellationToken cancellationToken)
        {
            if (_certificateInfo is not null)
            {
                return _certificateInfo;
            }

            await _mutex.WaitAsync(cancellationToken);

            try
            {
                if (_certificateInfo is null)
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    _logger.LogTrace(Resources.FetchingCertificate);

                    KeyVaultCertificate certificate;
                    if (string.IsNullOrEmpty(_certificateVersion))
                    {
                        Response<KeyVaultCertificateWithPolicy> response = await _certificateClient.GetCertificateAsync(_certificateName, cancellationToken);
                        certificate = response.Value;
                    }
                    else
                    {
                        Response<KeyVaultCertificate> response =
                            await _certificateClient.GetCertificateVersionAsync(_certificateName, _certificateVersion, cancellationToken);
                        certificate = response.Value;
                    }

                    KeyVaultKeyIdentifier keyIdentifier = GetKeyIdentifier(certificate);
                    X509Certificate2 x509Certificate = new(certificate.Cer);

                    _logger.LogTrace(Resources.FetchedCertificate, stopwatch.Elapsed.TotalMilliseconds);
                    _logger.LogTrace($"{Resources.CertificateDetails}{Environment.NewLine}{x509Certificate.ToString(verbose: true)}");

                    _certificateInfo = new CertificateInfo(x509Certificate, keyIdentifier);
                }
            }
            finally
            {
                _mutex.Release();
            }

            return _certificateInfo;
        }

        private KeyVaultKeyIdentifier GetKeyIdentifier(KeyVaultCertificate certificate)
        {
            Uri keyId;

            try
            {
                keyId = certificate.KeyId;
            }
            catch (Exception exception) when (exception is ArgumentNullException or UriFormatException)
            {
                throw new InvalidOperationException(Resources.InvalidCertificateKeyIdentifier, exception);
            }

            if (!KeyVaultKeyIdentifier.TryCreate(keyId, out KeyVaultKeyIdentifier keyIdentifier) ||
                !string.Equals(keyId.Segments[1].TrimEnd('/'), "keys", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(keyIdentifier.Version) ||
                Uri.Compare(
                    _certificateClient.VaultUri,
                    keyIdentifier.VaultUri,
                    UriComponents.SchemeAndServer,
                    UriFormat.Unescaped,
                    StringComparison.OrdinalIgnoreCase) != 0)
            {
                throw new InvalidOperationException(Resources.InvalidCertificateKeyIdentifier);
            }

            return keyIdentifier;
        }

        private sealed class CertificateInfo
        {
            internal X509Certificate2 Certificate { get; }
            internal KeyVaultKeyIdentifier KeyIdentifier { get; }

            internal CertificateInfo(X509Certificate2 certificate, KeyVaultKeyIdentifier keyIdentifier)
            {
                Certificate = certificate;
                KeyIdentifier = keyIdentifier;
            }
        }
    }
}
