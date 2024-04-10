// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography.X509Certificates;

namespace Sign.Core.Test
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class CertificatesFixture
    {
        private const string Name = "Certificates Tests";

        private readonly CertificateAuthority _timestampRootCa;
        private readonly CertificateAuthority _timestampIntermediateCa;
        private readonly X509Certificate2 _timestampingRootCertificate;
        private readonly X509Certificate2 _timestampingIntermediateCertificate;
        private readonly TimestampService _timestampService;

        internal Uri TimestampServiceUrl { get; }
        internal X509Certificate TimestampServiceCertificate
        {
            get => _timestampService.Certificate;
        }

        public CertificatesFixture()
        {
            TestUtility.RemoveTestIntermediateCertificates();

            ITestServer server = TestServerFixture.Instance;

            CertificateAuthority.BuildPrivatePki(
                PkiOptions.AllRevocation,
                server,
                out CertificateAuthority timestampRootCa,
                out CertificateAuthority timestampIntermediateCa,
                out X509Certificate2 timestampEndEntity,
                testName: "Test TSA",
                registerAuthorities: true);

            timestampEndEntity.Dispose();

            _timestampRootCa = timestampRootCa;
            _timestampIntermediateCa = timestampIntermediateCa;
            _timestampingRootCertificate = _timestampRootCa.CloneIssuerCert();
            _timestampingIntermediateCertificate = timestampIntermediateCa.CloneIssuerCert();

            _timestampService = TimestampService.Create(timestampIntermediateCa, server);

            TimestampServiceUrl = _timestampService.Url;
        }

        public void Dispose()
        {
            _timestampRootCa.Dispose();
            _timestampIntermediateCa.Dispose();
            _timestampingRootCertificate.Dispose();
            _timestampingIntermediateCertificate.Dispose();
            _timestampService.Dispose();

            TestUtility.RemoveTestIntermediateCertificates();
        }
    }
}