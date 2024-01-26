// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core
{
    internal sealed class CertificateManagerService : ICertificateStoreService
    {
        private string? _sha1Thumbprint;
        private string? _cryptoServiceProvider;
        private string? _privateKeyContainer;
        private string? _privateMachineKeyContainer;
        private readonly ILogger<ICertificateStoreService> _logger;

        // Dependency injection requires a public constructor.
        public CertificateManagerService(ILogger<ICertificateStoreService> logger)
        {
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _logger = logger;
        }

        /// <summary>
        /// Gets a certificate from either the machine or user certificate store using a SHA1 Thumbprint.
        /// </summary>
        /// <returns>A <see cref="X509Certificate2"/> certificate specified by a SHA1 Thumbprint.</returns>
        /// <exception cref="ArgumentException">Thrown when the SHA1 thumbprint wasn't found in any store.</exception>
        public Task<X509Certificate2> GetCertificateAsync()
        {
            ThrowIfUninitialized();

            if (TryFindCertificate(StoreLocation.LocalMachine, _sha1Thumbprint!, out X509Certificate2? certificate)
                || TryFindCertificate(StoreLocation.CurrentUser, _sha1Thumbprint!, out certificate))
            {
                return Task.FromResult(certificate);
            }

            throw new ArgumentException(Resources.CertificateNotFound);
        }


        [SupportedOSPlatform("windows")] // CspParameters is Windows-only but project uses cross platform frameworks. Dotnet/Sign is Windows only
        public async Task<AsymmetricAlgorithm> GetRsaAsync()
        {
            ThrowIfUninitialized();

            // Get RSA from a 3rd party cryptographic service provider
            if (!string.IsNullOrEmpty(_privateMachineKeyContainer))
            {
                var cspOptions = new CspParameters();

                cspOptions.KeyContainerName = _privateMachineKeyContainer;
                cspOptions.Flags = CspProviderFlags.UseMachineKeyStore;

                RSACryptoServiceProvider.UseMachineKeyStore = true;

                return new RSACryptoServiceProvider(cspOptions);
            }
            else if (!string.IsNullOrEmpty(_privateKeyContainer))
            {
                var cspOptions = new CspParameters();

                cspOptions.KeyContainerName = _privateKeyContainer;
                cspOptions.Flags = CspProviderFlags.UseDefaultKeyContainer;

                return new RSACryptoServiceProvider(cspOptions);
            }

            // Try to retrieve the certificate's private key.
            // EDCSA uses "1.2.840.10045.2.1";
            const string RSA = "1.2.840.113549.1.1.1";

            var certificate = await GetCertificateAsync();
            var keyAlgorithm = certificate.GetKeyAlgorithm();

            switch (keyAlgorithm)
            {
                case RSA:
                    return certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException(Resources.CertificateRSANotFound);
                default:
                    throw new InvalidOperationException(Resources.CertificateUnknownSignAlgoError);
            }
        }

        public void Initialize(string sha1Thumbprint, string? cryptoServiceProvider, string? privateKeyContainer,
            string? privateMachineKeyContainer)
        {
            // CSP requires either K or KM options but not both.
            if (!string.IsNullOrEmpty(cryptoServiceProvider)
                && string.IsNullOrEmpty(privateKeyContainer) == string.IsNullOrEmpty(privateMachineKeyContainer))
            {
                if (string.IsNullOrEmpty(privateKeyContainer) && string.IsNullOrEmpty(privateMachineKeyContainer))
                {
                    _logger.LogError(Resources.MultiplePrivateKeyContainersError);
                    throw new ArgumentException(Resources.MultiplePrivateKeyContainersError);
                }
                else
                {
                    // Both were provided but can only use one.
                    _logger.LogError(Resources.NoPrivateKeyContainerError);
                    throw new ArgumentException(Resources.NoPrivateKeyContainerError);
                }
            }
            
            if (string.IsNullOrEmpty(sha1Thumbprint))
            {
                _logger.LogError(Resources.InvalidSha1ThumbrpintValue);
                throw new ArgumentException(Resources.InvalidSha1ThumbrpintValue);
            }

            _sha1Thumbprint = sha1Thumbprint;
            _cryptoServiceProvider = cryptoServiceProvider;
            _privateKeyContainer = privateKeyContainer;
            _privateMachineKeyContainer = privateMachineKeyContainer;
        }

        private void ThrowIfUninitialized()
        {
            ArgumentNullException.ThrowIfNull(_sha1Thumbprint, nameof(_sha1Thumbprint));

            // Only SHA1 is required.
            if (string.IsNullOrEmpty(_sha1Thumbprint))
            {
                throw new ArgumentException(Resources.ValueCannotBeEmptyString, nameof(_sha1Thumbprint));
            }
        }

        public bool IsInitialized() => !string.IsNullOrEmpty(_sha1Thumbprint);

        private static bool TryFindCertificate(StoreLocation storeLocation, string sha1Fingerprint, [NotNullWhen(true)] out X509Certificate2? certificate)
        {
            // Check machine certificate store.
            using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);
                var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, sha1Fingerprint, validOnly: false);

                if (certificates.Count > 0)
                {
                    certificate = certificates[0];
                    return true;
                }

                certificate = null;
                return false;
            }
        }

    }
}
