#pragma warning disable IDE0073
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Sign.Core.Test
{
    /*
        From RFC 3280 (https://tools.ietf.org/html/rfc3280#section-4.1.1.2):

            AlgorithmIdentifier ::= SEQUENCE {
                algorithm               OBJECT IDENTIFIER,
                parameters              ANY DEFINED BY algorithm OPTIONAL
            }
	*/
    internal sealed class AlgorithmIdentifier
    {
        internal Oid Algorithm { get; }
        internal ReadOnlyMemory<byte>? Parameters { get; }

        internal AlgorithmIdentifier(Oid algorithm)
        {
            ArgumentNullException.ThrowIfNull(algorithm, nameof(algorithm));

            Algorithm = algorithm;
        }

        internal static AlgorithmIdentifier Decode(AsnReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader, nameof(reader));

            AsnReader algIdReader = reader.ReadSequence();
            Oid algorithm = new(algIdReader.ReadObjectIdentifier());

            // For all algorithms we currently support, parameter must be null.
            // However, presence of a DER encoded NULL value is optional.
            if (algIdReader.HasData)
            {
                algIdReader.ReadNull();
            }

            algIdReader.ThrowIfNotEmpty();

            return new AlgorithmIdentifier(algorithm);
        }

        internal ReadOnlyMemory<byte> Encode()
        {
            AsnWriter writer = new(AsnEncodingRules.DER);

            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(Algorithm.Value!);

                if (Parameters.HasValue)
                {
                    writer.WriteEncodedValue(Parameters.Value.Span);
                }
            }

            return writer.Encode();
        }
    }
}