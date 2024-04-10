#pragma warning disable IDE0073
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Sign.Core.Test
{
    /*
        From RFC 5126 (https://tools.ietf.org/html/rfc5126.html#section-5.11.1):

            CommitmentTypeIndication ::= SEQUENCE {
              commitmentTypeId CommitmentTypeIdentifier,
              commitmentTypeQualifier SEQUENCE SIZE (1..MAX) OF
                             CommitmentTypeQualifier OPTIONAL}

            CommitmentTypeIdentifier ::= OBJECT IDENTIFIER
	*/
    internal sealed class CommitmentTypeIndication
    {
        internal Oid CommitmentTypeId { get; }
        internal IReadOnlyList<CommitmentTypeQualifier>? Qualifiers { get; }

        internal CommitmentTypeIndication(
            Oid commitmentTypeId,
            IReadOnlyList<CommitmentTypeQualifier>? qualifiers = null)
        {
            ArgumentNullException.ThrowIfNull(commitmentTypeId, nameof(commitmentTypeId));

            CommitmentTypeId = commitmentTypeId;
            Qualifiers = qualifiers;
        }

        internal static CommitmentTypeIndication Decode(ReadOnlyMemory<byte> bytes)
        {
            AsnReader reader = new(bytes, AsnEncodingRules.DER);

            return Decode(reader);
        }

        internal static CommitmentTypeIndication Decode(AsnReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader, nameof(reader));

            AsnReader indicationReader = reader.ReadSequence();
            Oid commitmentTypeId = new(indicationReader.ReadObjectIdentifier());
            List<CommitmentTypeQualifier>? qualifiers = null;

            if (indicationReader.HasData)
            {
                AsnReader qualifierReader = indicationReader.ReadSequence();

                qualifiers = new List<CommitmentTypeQualifier>();

                while (qualifierReader.HasData)
                {
                    CommitmentTypeQualifier qualifier = CommitmentTypeQualifier.Decode(qualifierReader);

                    qualifiers.Add(qualifier);
                }

                if (qualifiers.Count == 0)
                {
                    throw new AsnContentException();
                }

                qualifierReader.ThrowIfNotEmpty();
            }

            indicationReader.ThrowIfNotEmpty();

            return new CommitmentTypeIndication(commitmentTypeId, qualifiers);
        }

        internal ReadOnlyMemory<byte> Encode()
        {
            AsnWriter writer = new(AsnEncodingRules.DER);

            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(CommitmentTypeId.Value!);

                if (Qualifiers is not null && Qualifiers.Count > 0)
                {
                    using (writer.PushSequence())
                    {
                        foreach (CommitmentTypeQualifier qualifier in Qualifiers)
                        {
                            ReadOnlyMemory<byte> encoded = qualifier.Encode();

                            writer.WriteEncodedValue(encoded.Span);
                        }
                    }
                }
            }

            return writer.Encode();
        }
    }
}