#pragma warning disable IDE0073
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Formats.Asn1;

namespace Sign.Core.Test
{
    /*
        From RFC 2634 (https://tools.ietf.org/html/rfc2634#section-5.4.1):

            ESSCertID ::=  SEQUENCE {
                certHash                 Hash,
                issuerSerial             IssuerSerial OPTIONAL
            }

            Hash ::= OCTET STRING -- SHA1 hash of entire certificate

            IssuerSerial ::= SEQUENCE {
                issuer                   GeneralNames,
                serialNumber             CertificateSerialNumber
            }
	*/
    internal sealed class EssCertId
    {
        internal ReadOnlyMemory<byte> CertificateHash { get; }
        internal IssuerSerial? IssuerSerial { get; }

        private EssCertId(ReadOnlyMemory<byte> hash, IssuerSerial? issuerSerial)
        {
            CertificateHash = hash;
            IssuerSerial = issuerSerial;
        }

        internal static EssCertId Decode(AsnReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader, nameof(reader));

            AsnReader sequenceReader = reader.ReadSequence();
            byte[] hash = sequenceReader.ReadOctetString();
            IssuerSerial? issuerSerial = null;

            if (sequenceReader.HasData)
            {
                issuerSerial = IssuerSerial.Decode(sequenceReader);

                sequenceReader.ThrowIfNotEmpty();
            }

            return new EssCertId(hash, issuerSerial);
        }
    }
}