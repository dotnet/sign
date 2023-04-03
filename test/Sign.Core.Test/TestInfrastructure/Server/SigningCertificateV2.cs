#pragma warning disable IDE0073
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core.Test
{
    /*
        From RFC 5035 (https://tools.ietf.org/html/rfc5035):

            SigningCertificateV2 ::= SEQUENCE {
                certs        SEQUENCE OF ESSCertIDv2,
                policies     SEQUENCE OF PolicyInformation OPTIONAL
            }
	*/
    internal sealed class SigningCertificateV2
    {
        internal IReadOnlyList<EssCertIdV2> Certificates { get; }
        internal IReadOnlyList<PolicyInformation>? Policies { get; }

        private SigningCertificateV2(
            IReadOnlyList<EssCertIdV2> certificates,
            IReadOnlyList<PolicyInformation>? policies)
        {
            Certificates = certificates;
            Policies = policies;
        }

        internal static SigningCertificateV2 Create(X509Certificate2 certificate, HashAlgorithmName hashAlgorithmName)
        {
            ArgumentNullException.ThrowIfNull(certificate, nameof(certificate));

            EssCertIdV2 essCertIdV2 = EssCertIdV2.Create(certificate, hashAlgorithmName);

            return new SigningCertificateV2(new[] { essCertIdV2 }, policies: null);
        }

        internal static SigningCertificateV2 Decode(ReadOnlyMemory<byte> bytes)
        {
            AsnReader reader = new(bytes, AsnEncodingRules.DER);

            return Decode(reader);
        }

        internal static SigningCertificateV2 Decode(AsnReader reader)
        {
            AsnReader sequenceReader = reader.ReadSequence();

            IReadOnlyList<EssCertIdV2> certificates = ReadCertificates(sequenceReader);
            IReadOnlyList<PolicyInformation>? policies = null;

            if (sequenceReader.HasData)
            {
                policies = ReadPolicies(sequenceReader);
            }

            sequenceReader.ThrowIfNotEmpty();

            return new SigningCertificateV2(certificates, policies);
        }

        internal ReadOnlyMemory<byte> Encode()
        {
            AsnWriter writer = new(AsnEncodingRules.DER);

            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    foreach (EssCertIdV2 certificate in Certificates)
                    {
                        writer.WriteEncodedValue(certificate.Encode().Span);
                    }
                }

                if (Policies is not null && Policies.Count > 0)
                {
                    using (writer.PushSequence())
                    {
                        foreach (PolicyInformation policy in Policies)
                        {
                            writer.WriteEncodedValue(policy.Encode().Span);
                        }
                    }
                }
            }

            return writer.Encode();
        }

        private static IReadOnlyList<EssCertIdV2> ReadCertificates(AsnReader reader)
        {
            AsnReader essCertIdV2Reader = reader.ReadSequence();
            List<EssCertIdV2> certificates = new();

            while (essCertIdV2Reader.HasData)
            {
                EssCertIdV2 certificate = EssCertIdV2.Decode(essCertIdV2Reader);

                certificates.Add(certificate);
            }

            essCertIdV2Reader.ThrowIfNotEmpty();

            return certificates;
        }

        private static IReadOnlyList<PolicyInformation> ReadPolicies(AsnReader reader)
        {
            AsnReader policiesReader = reader.ReadSequence();
            List<PolicyInformation> policies = new();

            while (policiesReader.HasData)
            {
                PolicyInformation policy = PolicyInformation.Decode(policiesReader);

                policies.Add(policy);
            }

            policiesReader.ThrowIfNotEmpty();

            return policies;
        }
    }
}