// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core
{
    /// <summary>
    /// Top-level interface for certificate services such as Azure Key Vault and Windows Certificate Manager.
    /// </summary>
    internal interface ICertificateService
    {
        /// <summary>
        /// Acquires the certificate in the initialized store.
        /// </summary>
        /// <returns></returns>
        Task<X509Certificate2> GetCertificateAsync();

        /// <summary>
        /// Creates or acquires the RSA for the specified certificate.
        /// </summary>
        /// <returns>An <see cref="AsymmetricAlgorithm"/> containing the RSA or ECDsa object.</returns>
        Task<AsymmetricAlgorithm> GetRsaAsync();


        /// <summary>
        /// Checks if the underlying certificate service has been initialized by the signer class.
        /// </summary>
        /// <returns></returns>
        bool IsInitialized();
    }
}