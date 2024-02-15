// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Resources;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core
{
    internal sealed class CertificateStoreService : ISignatureAlgorithmProvider, ICertificateProvider
    {
        private readonly string _sha1Thumbprint;
        private readonly string? _cryptoServiceProvider;
        private readonly string? _privateKeyContainer;
        private readonly string? _pfxFilePath;
        private readonly string? _pfxFilePassword;
        private readonly bool _isPrivateMachineKeyContainer;

        private readonly ILogger<CertificateStoreService> _logger;

        // Dependency injection requires a public constructor.
        internal CertificateStoreService(
            IServiceProvider serviceProvider,
            string sha1Thumbprint,
            string? cryptoServiceProvider,
            string? privateKeyContainer,
            string? pfxFilePath,
            string? pfxFilePassword,
            bool isPrivateMachineKeyContainer
            )
        {
            ArgumentNullException.ThrowIfNull(sha1Thumbprint, nameof(sha1Thumbprint));

            _sha1Thumbprint = sha1Thumbprint;
            _cryptoServiceProvider = cryptoServiceProvider;
            _privateKeyContainer = privateKeyContainer;
            _isPrivateMachineKeyContainer = isPrivateMachineKeyContainer;
            _pfxFilePath = pfxFilePath;
            _pfxFilePassword = pfxFilePassword;

            _logger = serviceProvider.GetRequiredService<ILogger<CertificateStoreService>>();
        }


        [SupportedOSPlatform("windows")] // CspParameters is Windows-only but project uses cross platform frameworks. Dotnet/Sign is Windows only
        public async Task<RSA> GetRsaAsync(CancellationToken cancellationToken)
        {
            // Get RSA from a 3rd party cryptographic service provider
            if (!string.IsNullOrEmpty(_privateKeyContainer))
            {
                var cspOptions = new CspParameters();

                cspOptions.ProviderName = _cryptoServiceProvider;
                cspOptions.ProviderType = 1; // RSA = 1 DSA = 13
                cspOptions.KeyContainerName = _privateKeyContainer;

                if (_isPrivateMachineKeyContainer)
                {
                    cspOptions.Flags = CspProviderFlags.UseMachineKeyStore;

                    RSACryptoServiceProvider.UseMachineKeyStore = true;
                }
                else
                {
                    cspOptions.Flags = CspProviderFlags.UseDefaultKeyContainer;
                }

                return new RSACryptoServiceProvider(cspOptions);
            }

            // Certificate wasn't in CSP. Attempt to extract from store or provided file.
            const string RSA = "1.2.840.113549.1.1.1";

            var certificate = await GetStoreCertificateAsync();
            var keyAlgorithm = certificate.GetKeyAlgorithm();

            switch (keyAlgorithm)
            {
                case RSA:
                    return certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException(Resources.CertificateRSANotFound);
                default:
                    throw new InvalidOperationException(Resources.UnsupportedPublicKeyAlgorithm);
            }
        }

        /// <summary>
        /// Gets a certificate from a local (user or machine) certificate store or from a provided PFX file.
        /// </summary>
        /// <returns>A <see cref="X509Certificate2"/> certificate specified by a SHA1 Thumbprint.</returns>
        /// <exception cref="ArgumentException">Thrown when the SHA1 thumbprint wasn't found in any store.</exception>
        public async Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken)
            => await GetStoreCertificateAsync();

        private Task<X509Certificate2> GetStoreCertificateAsync()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            _logger.LogTrace(Resources.FetchingCertificate);

            // Check the provided file if any.
            if (!string.IsNullOrEmpty(_pfxFilePath))
            {
                var certCollection = new X509Certificate2Collection();
                certCollection.Import(_pfxFilePath, _pfxFilePassword, X509KeyStorageFlags.EphemeralKeySet);

                foreach (var cert in certCollection)
                {
                    if (string.Equals(cert.Thumbprint, _sha1Thumbprint, StringComparison.InvariantCultureIgnoreCase))
                    {
                        _logger.LogTrace(Resources.FetchedCertificate, stopwatch.Elapsed.TotalMilliseconds);

                        return Task.FromResult(new X509Certificate2(cert));
                    }
                }

                throw new ArgumentException(string.Format(Resources.CertificateNotFoundInFile, Path.GetFileName(_pfxFilePath)));
            }

            // Search User or Machine certificate stores.
            if (!string.IsNullOrEmpty(_privateKeyContainer)
                && _isPrivateMachineKeyContainer
                    ? TryFindCertificate(StoreLocation.LocalMachine, _sha1Thumbprint!, out X509Certificate2? certificate)
                    : TryFindCertificate(StoreLocation.CurrentUser, _sha1Thumbprint!, out certificate))
            {
                _logger.LogTrace(Resources.FetchedCertificate, stopwatch.Elapsed.TotalMilliseconds);

                return Task.FromResult(certificate);
            }

            _logger.LogTrace(Resources.FetchedCertificate, stopwatch.Elapsed.TotalMilliseconds);

            throw new ArgumentException(_isPrivateMachineKeyContainer ? Resources.CertificateNotFoundInMachineStore : Resources.CertificateNotFoundInUserStore);
        }

        private static bool TryFindCertificate(StoreLocation storeLocation, string sha1Fingerprint, [NotNullWhen(true)] out X509Certificate2? certificate)
        {
            // Check machine certificate store.
            using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);
                var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, sha1Fingerprint, validOnly: false);

                foreach (var cert in certificates)
                {
                    if (string.Equals(cert.Thumbprint, sha1Fingerprint, StringComparison.InvariantCultureIgnoreCase))
                    {
                        certificate = cert;

                        return true;
                    }
                }

                certificate = null;

                return false;
            }
        }

    }
}
