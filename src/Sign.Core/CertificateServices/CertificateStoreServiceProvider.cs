// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    /// <summary>
    /// Provider that initializes a new <see cref="CertificateStoreService"/> if required.
    /// </summary>
    internal class CertificateStoreServiceProvider
    {
        private readonly string _sha1Thumbprint;
        private readonly string? _cryptoServiceProvider;
        private readonly string? _privateKeyContainer;
        private readonly string? _pfxFilePath;
        private readonly string? _pfxFilePassword;
        private readonly bool _isMachineKeyContainer;

        private readonly object _lockObject = new();
        private CertificateStoreService? _certificateStoreService;

        /// <summary>
        /// Creates a new service provider for accessing certificates within a store.
        /// </summary>
        /// <param name="sha1Thumbprint">Required thumbprint used to identify the certificate in the store.</param>
        /// <param name="cryptoServiceProvider">Optional Cryptographic service provider used to access 3rd party certificate stores.</param>
        /// <param name="privateKeyContainer">Key Container stored in either the per-user or per-machine location.</param>
        /// <param name="isMachineKeyContainer">Flag used to denote per-machine key container should be used.</param>
        /// <exception cref="ArgumentException"></exception>
        internal CertificateStoreServiceProvider(
            string sha1Thumbprint,
            string? cryptoServiceProvider,
            string? privateKeyContainer,
            string? pfxFilePath,
            string? pfxFilePassword,
            bool isMachineKeyContainer)
        {
            ArgumentNullException.ThrowIfNull(sha1Thumbprint, nameof(sha1Thumbprint));

            if (string.IsNullOrEmpty(sha1Thumbprint))
            {
                throw new ArgumentException(Resources.ValueCannotBeEmptyString, nameof(sha1Thumbprint));
            }

            // Both or neither can be provided when accessing a certificate.
            if (!string.IsNullOrEmpty(cryptoServiceProvider) == string.IsNullOrEmpty(privateKeyContainer))
            {
                throw new ArgumentException(
                    Resources.ValueCannotBeEmptyString,
                    string.IsNullOrEmpty(cryptoServiceProvider) ? nameof(cryptoServiceProvider) : nameof(privateKeyContainer));
            }

            _sha1Thumbprint = sha1Thumbprint;
            _cryptoServiceProvider = cryptoServiceProvider;
            _privateKeyContainer = privateKeyContainer;
            _isMachineKeyContainer = isMachineKeyContainer;
            _pfxFilePath = pfxFilePath;
            _pfxFilePassword = pfxFilePassword;
        }

        internal ISignatureAlgorithmProvider GetSignatureAlgorithmProvider(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));

            return GetService(serviceProvider);
        }

        internal ICertificateProvider GetCertificateProvider(IServiceProvider serviceProvider)
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

                _certificateStoreService = new CertificateStoreService(serviceProvider,_sha1Thumbprint, _cryptoServiceProvider, _privateKeyContainer, _pfxFilePath, _pfxFilePassword, _isMachineKeyContainer);
            }

            return _certificateStoreService;
        }
    }
}
