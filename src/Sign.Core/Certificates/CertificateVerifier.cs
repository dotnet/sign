// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class CertificateVerifier : ICertificateVerifier
    {
        private readonly ILogger _logger;

        // Dependency injection requires a public constructor.
        public CertificateVerifier(ILogger<ICertificateVerifier> logger)
        {
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _logger = logger;
        }

        public void Verify(X509Certificate2 certificate)
        {
            ArgumentNullException.ThrowIfNull(certificate, nameof(certificate));

            DateTime now = DateTime.Now;

            if (now < certificate.NotBefore)
            {
                // See https://github.com/dotnet/roslyn-analyzers/issues/5626
#pragma warning disable CA2254 // Template should be a static expression
                _logger.LogWarning(Resources.CertificateIsNotYetTimeValid);
            }
            else if (certificate.NotAfter < now)
            {
                _logger.LogWarning(Resources.CertificateIsExpired);
#pragma warning restore CA2254 // Template should be a static expression
            }
        }
    }
}