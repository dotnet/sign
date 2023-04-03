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

            ESSCertIDv2 ::= SEQUENCE {
                hashAlgorithm            AlgorithmIdentifier
                       DEFAULT {algorithm id-sha256},
                certHash                 Hash,
                issuerSerial             IssuerSerial OPTIONAL
            }

            Hash ::= OCTET STRING

            IssuerSerial ::= SEQUENCE {
                issuer                   GeneralNames,
                serialNumber             CertificateSerialNumber
           }
	*/
    internal sealed class EssCertIdV2
    {
        private static readonly AlgorithmIdentifier DefaultHashAlgorithm = new(Oids.Sha256);

        internal AlgorithmIdentifier HashAlgorithm { get; }
        internal ReadOnlyMemory<byte> CertificateHash { get; }
        internal IssuerSerial? IssuerSerial { get; }

        internal EssCertIdV2(
            AlgorithmIdentifier? hashAlgorithm,
            ReadOnlyMemory<byte> hash,
            IssuerSerial? issuerSerial)
        {
            HashAlgorithm = hashAlgorithm ?? DefaultHashAlgorithm;
            CertificateHash = hash;
            IssuerSerial = issuerSerial;
        }

        internal static EssCertIdV2 Create(X509Certificate2 certificate, HashAlgorithmName hashAlgorithmName)
        {
            ArgumentNullException.ThrowIfNull(certificate, nameof(certificate));

            AlgorithmIdentifier algorithm = new(hashAlgorithmName.ToOid());
            ReadOnlyMemory<byte> hash = certificate.Hash(hashAlgorithmName);
            IssuerSerial issuerSerial = IssuerSerial.Create(certificate);

            return new EssCertIdV2(algorithm, hash, issuerSerial);
        }

        internal static EssCertIdV2 Decode(AsnReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader, nameof(reader));

            AsnReader sequenceReader = reader.ReadSequence();

            AlgorithmIdentifier algorithm;

            if (sequenceReader.PeekTag() == Asn1Tag.Sequence)
            {
                algorithm = AlgorithmIdentifier.Decode(sequenceReader);
            }
            else
            {
                algorithm = new AlgorithmIdentifier(Oids.Sha256);
            }

            byte[] hash = sequenceReader.ReadOctetString();
            IssuerSerial? issuerSerial = null;

            if (sequenceReader.HasData)
            {
                issuerSerial = IssuerSerial.Decode(sequenceReader);
            }

            sequenceReader.ThrowIfNotEmpty();

            return new EssCertIdV2(algorithm, hash, issuerSerial);
        }

        internal ReadOnlyMemory<byte> Encode()
        {
            AsnWriter writer = new(AsnEncodingRules.DER);

            using (writer.PushSequence())
            {
                if (HashAlgorithm is not null && !HashAlgorithm.Algorithm.IsEqualTo(Oids.Sha256))
                {
                    writer.WriteEncodedValue(HashAlgorithm.Encode().Span);
                }

                writer.WriteOctetString(CertificateHash.Span);

                if (IssuerSerial is not null)
                {
                    writer.WriteEncodedValue(IssuerSerial.Encode().Span);
                }
            }

            return writer.Encode();
        }
    }
}