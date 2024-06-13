// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Sign.Core;

namespace Sign.SignatureProviders.CertificateStore
{
    /// <summary>
    /// Provider that initializes a new <see cref="CertificateStoreService"/> if required.
    /// </summary>
    internal class CertificateStoreServiceProvider : ISignatureProvider
    {
        private readonly string _certificateFingerprint;
        private readonly HashAlgorithmName _certificateFingerprintAlgorithm;
        private readonly string? _cryptoServiceProvider;
        private readonly string? _privateKeyContainer;
        private readonly string? _certificateFilePath;
        private readonly string? _certificateFilePassword;
        private readonly bool _isMachineKeyContainer;

        private readonly object _lockObject = new();
        private CertificateStoreService? _certificateStoreService;

        /// <summary>
        /// Creates a new service provider for accessing certificates within a store.
        /// </summary>
        /// <param name="certificateFingerprint">Required fingerprint used to identify the certificate in the store.</param>
        /// <param name="certificateFingerprintAlgorithm">Fingerprint algorithm used to identify the algorithm type of the fingerprint.</param>
        /// <param name="cryptoServiceProvider">Optional Cryptographic service provider used to access 3rd party certificate stores.</param>
        /// <param name="privateKeyContainer">Optional Key Container stored in either the per-user or per-machine location.</param>
        /// <param name="certificateFilePath">Optional path to the PFX, P7B, or CER file with the certificate.</param>
        /// <param name="certificateFilePassword">Optional password used to open the provided certificate.</param>
        /// <param name="isMachineKeyContainer">Optional Flag used to denote per-machine key container should be used.</param>
        /// <exception cref="ArgumentException">Thrown when a required argument is empty not valid.</exception>
        internal CertificateStoreServiceProvider(
            string certificateFingerprint,
            HashAlgorithmName certificateFingerprintAlgorithm,
            string? cryptoServiceProvider,
            string? privateKeyContainer,
            string? certificateFilePath,
            string? certificateFilePassword,
            bool isMachineKeyContainer)
        {
            ArgumentException.ThrowIfNullOrEmpty(certificateFingerprint, nameof(certificateFingerprint));

            // Both or neither can be provided when accessing a certificate.
            if (!string.IsNullOrEmpty(cryptoServiceProvider) == string.IsNullOrEmpty(privateKeyContainer))
            {
                throw new ArgumentException(
                    Resources.ValueCannotBeEmptyString,
                    string.IsNullOrEmpty(cryptoServiceProvider) ? nameof(cryptoServiceProvider) : nameof(privateKeyContainer));
            }

            _certificateFingerprint = certificateFingerprint;
            _certificateFingerprintAlgorithm = certificateFingerprintAlgorithm;
            _cryptoServiceProvider = cryptoServiceProvider;
            _privateKeyContainer = privateKeyContainer;
            _isMachineKeyContainer = isMachineKeyContainer;
            _certificateFilePath = certificateFilePath;
            _certificateFilePassword = certificateFilePassword;
        }

        public ISignatureAlgorithmProvider GetSignatureAlgorithmProvider(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));

            return GetService(serviceProvider);
        }

        public ICertificateProvider GetCertificateProvider(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));

            return GetService(serviceProvider);
        }

        private CertificateStoreService GetService(IServiceProvider serviceProvider)
        {
            if (_certificateStoreService is not null)
            {
                return _certificateStoreService;
            }

            lock (_lockObject)
            {
                if (_certificateStoreService is not null)
                {
                    return _certificateStoreService;
                }

                _certificateStoreService = new CertificateStoreService(
                    serviceProvider,
                    _certificateFingerprint,
                    _certificateFingerprintAlgorithm,
                    _cryptoServiceProvider,
                    _privateKeyContainer,
                    _certificateFilePath,
                    _certificateFilePassword,
                    _isMachineKeyContainer);
            }

            return _certificateStoreService;
        }
    }
}
