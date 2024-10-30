// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Sign.TestInfrastructure
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class TrustedCertificateFixture : IDisposable
    {
        private const string Name = nameof(TrustedCertificateFixture);

        private readonly EphemeralTrust? _ephemeralTrust;
        private readonly X509Certificate2? _certificate;

        public X509Certificate2 TrustedCertificate
        {
            get
            {
                if (Environment.IsPrivilegedProcess)
                {
                    return _certificate!;
                }

                throw new UnauthorizedAccessException("This test requires elevation.");
            }
        }

        [SupportedOSPlatform("windows")]
        public TrustedCertificateFixture()
        {
            if (Environment.IsPrivilegedProcess)
            {
                _certificate = SelfIssuedCertificateCreator.CreateCertificate();
                _ephemeralTrust = new EphemeralTrust(_certificate);
            }
        }

        public void Dispose()
        {
            if (Environment.IsPrivilegedProcess)
            {
                _ephemeralTrust?.Dispose();
                TrustedCertificate?.Dispose();
            }
        }
    }
}
