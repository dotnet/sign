#pragma warning disable IDE0073
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core.Test
{
    /*
        From RFC 2634 (https://tools.ietf.org/html/rfc2634#section-5.4.1):

            IssuerSerial ::= SEQUENCE {
                issuer                   GeneralNames,
                serialNumber             CertificateSerialNumber
            }

        From RFC 2634 (https://tools.ietf.org/html/rfc3280#section-4.2.1.7):

            GeneralNames ::= SEQUENCE SIZE (1..MAX) OF GeneralName

        From RFC 3280 (https://tools.ietf.org/html/rfc3280#section-4.1):

            CertificateSerialNumber  ::=  INTEGER
	*/
    internal sealed class IssuerSerial
    {
        internal IReadOnlyList<GeneralName> GeneralNames { get; }
        internal ReadOnlyMemory<byte> SerialNumber { get; } // big endian

        internal IssuerSerial(IReadOnlyList<GeneralName> generalNames, ReadOnlyMemory<byte> serialNumber)
        {
            ArgumentNullException.ThrowIfNull(generalNames, nameof(generalNames));

            GeneralNames = generalNames;
            SerialNumber = serialNumber;
        }

        internal static IssuerSerial Create(X509Certificate2 certificate)
        {
            ArgumentNullException.ThrowIfNull(certificate, nameof(certificate));

            GeneralName[] generalNames = new[] { new GeneralName(certificate.IssuerName) };
            byte[] serialNumber = certificate.GetSerialNumber();

            // Convert from little endian to big endian.
            Array.Reverse(serialNumber);

            return new IssuerSerial(generalNames, serialNumber);
        }

        internal static IssuerSerial Decode(AsnReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader, nameof(reader));

            AsnReader sequenceReader = reader.ReadSequence();
            IReadOnlyList<GeneralName> generalNames = ReadGeneralNames(sequenceReader);
            ReadOnlyMemory<byte> serialNumber = sequenceReader.ReadIntegerBytes();

            sequenceReader.ThrowIfNotEmpty();

            return new IssuerSerial(generalNames, serialNumber);
        }

        internal ReadOnlyMemory<byte> Encode()
        {
            // Per RFC 5280 section 4.1.2.2 (https://tools.ietf.org/html/rfc5280#section-4.1.2.2)
            // serial number must be an unsigned integer.
            AsnWriter writer = new(AsnEncodingRules.DER);

            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    foreach (GeneralName generalName in GeneralNames)
                    {
                        ReadOnlyMemory<byte> encoded = generalName.Encode();

                        writer.WriteEncodedValue(encoded.Span);
                    }
                }

                writer.WriteIntegerUnsigned(SerialNumber.Span);
            }

            return writer.Encode();
        }

        private static IReadOnlyList<GeneralName> ReadGeneralNames(AsnReader reader)
        {
            AsnReader sequenceReader = reader.ReadSequence();
            List<GeneralName> generalNames = new(capacity: 1);

            GeneralName? generalName = GeneralName.Decode(sequenceReader);

            if (generalName is not null)
            {
                generalNames.Add(generalName);
            }

            sequenceReader.ThrowIfNotEmpty();

            return generalNames;
        }
    }
}