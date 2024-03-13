// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    /// <summary>
    /// Creates an object used to access Certificate Stores and acquire certificates and RSA keys (if applicable).
    /// </summary>
    internal sealed class CertificateStoreService : ISignatureAlgorithmProvider, ICertificateProvider
    {
        private readonly string _sha1Thumbprint;
        private readonly string? _cryptoServiceProvider;
        private readonly string? _privateKeyContainer;
        private readonly string? _certificatePath;
        private readonly string? _certificatePassword;
        private readonly bool _isPrivateMachineKeyContainer;

        private readonly ILogger<CertificateStoreService> _logger;

        internal CertificateStoreService(
            IServiceProvider serviceProvider,
            string sha1Thumbprint,
            string? cryptoServiceProvider,
            string? privateKeyContainer,
            string? certificatePath,
            string? certificatePassword,
            bool isPrivateMachineKeyContainer)
        {
            if (string.IsNullOrEmpty(sha1Thumbprint))
            {
                throw new ArgumentException(Resources.ValueCannotBeEmptyString, nameof(sha1Thumbprint));
            }

            _sha1Thumbprint = sha1Thumbprint;
            _cryptoServiceProvider = cryptoServiceProvider;
            _privateKeyContainer = privateKeyContainer;
            _isPrivateMachineKeyContainer = isPrivateMachineKeyContainer;
            _certificatePath = certificatePath;
            _certificatePassword = certificatePassword;

            _logger = serviceProvider.GetRequiredService<ILogger<CertificateStoreService>>();
        }


        /// <summary>
        /// Acquires the RSA private key from either a CSP registered in the machine or from the certificate provided by the user.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for this asynchronous task.</param>
        /// <returns><see cref="RSA"/> algorithm object used to acquire the private key.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the provided key is not RSA.</exception>
        [SupportedOSPlatform("windows")] // CspParameters is Windows-only but project uses cross platform frameworks. Dotnet/Sign is Windows only
        public async Task<RSA> GetRsaAsync(CancellationToken cancellationToken)
        {
            // Get RSA from a cryptographic service provider
            if (!string.IsNullOrEmpty(_privateKeyContainer) && !string.IsNullOrEmpty(_cryptoServiceProvider))
            {
                var cngKeyFlags = CngKeyOpenOptions.Silent;

                if (_isPrivateMachineKeyContainer)
                {
                    cngKeyFlags |= CngKeyOpenOptions.MachineKey;

                    RSACryptoServiceProvider.UseMachineKeyStore = true;
                }
                else
                {
                    cngKeyFlags |= CngKeyOpenOptions.UserKey;
                }

                using CngKey cngKey = CngKey.Open(
                    _privateKeyContainer,
                    new CngProvider(_cryptoServiceProvider), cngKeyFlags);

                return new RSACng(cngKey);
            }

            // Certificate wasn't in CSP. Attempt to extract from store or provided file.
            const string RSA = "1.2.840.113549.1.1.1";

            using X509Certificate2 certificate = await GetStoreCertificateAsync();
            string keyAlgorithm = certificate.GetKeyAlgorithm();

            switch (keyAlgorithm)
            {
                case RSA:
                    return certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException(Resources.CertificateRSANotFound);
                default:
                    throw new InvalidOperationException(Resources.UnsupportedPublicKeyAlgorithm);
            }
        }

        /// <summary>
        /// Gets a certificate from a local (user or machine) certificate store or from a provided certificate file.
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
            if (!string.IsNullOrEmpty(_certificatePath))
            {
                var certCollection = new X509Certificate2Collection();
                certCollection.Import(_certificatePath, _certificatePassword, X509KeyStorageFlags.EphemeralKeySet);

                foreach (var cert in certCollection)
                {
                    if (string.Equals(cert.Thumbprint, _sha1Thumbprint, StringComparison.InvariantCultureIgnoreCase))
                    {
                        _logger.LogTrace(Resources.FetchedCertificate, stopwatch.Elapsed.TotalMilliseconds);

                        return Task.FromResult(new X509Certificate2(cert));
                    }
                }

                throw new ArgumentException(string.Format(Resources.CertificateNotFoundInFile, Path.GetFileName(_certificatePath)));
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
            using (var store = new X509Store(StoreName.My, storeLocation))
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