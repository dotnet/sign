// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core
{
    internal class CertificateManagerService : ICertificateManangerService
    {
        private string? _Sha1Thumbprint;
        private string? _cryptoServiceProvider;
        private string? _privateKeyContainer;
        private string? _privateMachineKeyContainer;
        private readonly ILogger<ICertificateManangerService> _logger;

        // Dependency injection requires a public constructor.
        public CertificateManagerService(ILogger<ICertificateManangerService> logger)
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

            // Check machine certificate store.
            using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);
                var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, _Sha1Thumbprint!, false);

                if (certificates.Count > 0)
                {
                    return Task.FromResult(certificates[0]);
                }
            }

            // Check current user certificate store.
            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);
                var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, _Sha1Thumbprint!, false);

                if (certificates.Count == 0)
                {
                    throw new ArgumentException(Resources.CertificateNotFound);
                }

                return Task.FromResult(certificates[0]);
            }
        }


        [SupportedOSPlatform("windows")] // CsCParameters is Windows-only but we support cross platform frameworks.
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

            // Extract RSA from Windows Cert Manager certificate.
            const string RSA = "1.2.840.113549.1.1.1";
            const string Ecc = "1.2.840.10045.2.1";

            var certificate = await GetCertificateAsync();
            var keyAlgorithm = certificate.GetKeyAlgorithm();

            switch (keyAlgorithm)
            {
                case RSA:
                    return certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException(Resources.CertificateRSANotFound);
                case Ecc:
                    return certificate.GetECDsaPrivateKey() ?? throw new InvalidOperationException(Resources.CertificateECDsaNotFound);
                default:
                    throw new InvalidOperationException(Resources.CertificateUnknownSignAlgoError);
            }
        }

        public void Initialize(string sha1Thumbprint, string? cryptoServiceProvider, string? privateKeyContainer,
            string? privateMachineKeyContainer)
        {
            this._Sha1Thumbprint = sha1Thumbprint;
            this._cryptoServiceProvider = cryptoServiceProvider;
            this._privateKeyContainer = privateKeyContainer;
            this._privateMachineKeyContainer = privateMachineKeyContainer;
        }

        private void ThrowIfUninitialized()
        {
            ArgumentNullException.ThrowIfNull(_Sha1Thumbprint, nameof(_Sha1Thumbprint));

            // Only SHA1 is required.
            if (string.IsNullOrEmpty(_Sha1Thumbprint))
            {
                throw new ArgumentException(Resources.ValueCannotBeEmptyString, nameof(_Sha1Thumbprint));
            }
        }

        public bool IsInitialized() => _Sha1Thumbprint != null;
    }
}
