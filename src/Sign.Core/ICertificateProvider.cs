// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography.X509Certificates;

namespace Sign.Core
{
    /// <summary>
    /// Top-level interface for certificate services such as Azure Key Vault and Certificate Store Manager.
    /// </summary>
    internal interface ICertificateProvider
    {
        /// <summary>
        /// Acquires the certificate from the initialized certificate service.
        /// </summary>
        /// <returns>An <see cref="X509Certificate2"/> certificate acquired from the initialized certificate service.</returns>
        Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Acquires additional certificates (e.g. intermediates) used to build the chain for the signing certificate.
        /// Providers that do not have a chain to expose return an empty collection.
        /// </summary>
        Task<X509Certificate2Collection> GetAdditionalCertificatesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new X509Certificate2Collection());
    }
}