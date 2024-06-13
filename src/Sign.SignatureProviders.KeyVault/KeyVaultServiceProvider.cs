// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Azure.Core;
using Sign.Core;

namespace Sign.SignatureProviders.KeyVault
{
    internal sealed class KeyVaultServiceProvider : ISignatureProvider
    {
        private readonly string _certificateName;
        private readonly Uri _keyVaultUrl;
        private readonly TokenCredential _tokenCredential;
        private readonly object _lockObject = new();
        private KeyVaultService? _keyVaultService;

        internal KeyVaultServiceProvider(
            TokenCredential tokenCredential,
            Uri keyVaultUrl,
            string certificateName)
        {
            ArgumentNullException.ThrowIfNull(tokenCredential, nameof(tokenCredential));
            ArgumentNullException.ThrowIfNull(keyVaultUrl, nameof(keyVaultUrl));
            ArgumentException.ThrowIfNullOrEmpty(certificateName, nameof(certificateName));

            _tokenCredential = tokenCredential;
            _keyVaultUrl = keyVaultUrl;
            _certificateName = certificateName;
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

        private KeyVaultService GetService(IServiceProvider serviceProvider)
        {
            if (_keyVaultService is not null)
            {
                return _keyVaultService;
            }

            lock (_lockObject)
            {
                if (_keyVaultService is not null)
                {
                    return _keyVaultService;
                }

                _keyVaultService = new KeyVaultService(serviceProvider, _tokenCredential, _keyVaultUrl, _certificateName);
            }

            return _keyVaultService;
        }
    }
}
