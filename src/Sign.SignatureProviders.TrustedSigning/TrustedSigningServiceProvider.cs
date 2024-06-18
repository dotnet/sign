// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sign.Core;

namespace Sign.SignatureProviders.TrustedSigning
{
    internal sealed class TrustedSigningServiceProvider : ISignatureProvider
    {
        private readonly TokenCredential _tokenCredential;
        private readonly Uri _endpointUrl;
        private readonly string _accountName;
        private readonly string _certificateProfileName;
        private readonly object _lockObject = new();
        private TrustedSigningService? _trustedSigningService;

        public TrustedSigningServiceProvider(
            TokenCredential tokenCredential,
            Uri endpointUrl,
            string accountName,
            string certificateProfileName)
        {
            ArgumentNullException.ThrowIfNull(tokenCredential, nameof(tokenCredential));
            ArgumentNullException.ThrowIfNull(endpointUrl, nameof(endpointUrl));
            ArgumentException.ThrowIfNullOrEmpty(accountName, nameof(accountName));
            ArgumentException.ThrowIfNullOrEmpty(certificateProfileName, nameof(certificateProfileName));

            _tokenCredential = tokenCredential;
            _endpointUrl = endpointUrl;
            _accountName = accountName;
            _certificateProfileName = certificateProfileName;
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

        private TrustedSigningService GetService(IServiceProvider serviceProvider)
        {
            if (_trustedSigningService is not null)
            {
                return _trustedSigningService;
            }

            lock (_lockObject)
            {
                if (_trustedSigningService is not null)
                {
                    return _trustedSigningService;
                }

                ILogger<TrustedSigningService> logger = serviceProvider.GetRequiredService<ILogger<TrustedSigningService>>();
                _trustedSigningService = new TrustedSigningService(_tokenCredential, _endpointUrl, _accountName, _certificateProfileName, logger);
            }

            return _trustedSigningService;
        }
    }
}
