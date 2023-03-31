#pragma warning disable IDE0073
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Sign.Core.Test
{
    /*
        From RFC 5280 (https://tools.ietf.org/html/rfc5280#appendix-A.2):

            PolicyQualifierInfo ::= SEQUENCE {
                policyQualifierId  PolicyQualifierId,
                qualifier          ANY DEFINED BY policyQualifierId }

            -- policyQualifierIds for Internet policy qualifiers

            id-qt          OBJECT IDENTIFIER ::=  { id-pkix 2 }
            id-qt-cps      OBJECT IDENTIFIER ::=  { id-qt 1 }
            id-qt-unotice  OBJECT IDENTIFIER ::=  { id-qt 2 }

            PolicyQualifierId ::= OBJECT IDENTIFIER ( id-qt-cps | id-qt-unotice )
	*/
    internal sealed class PolicyQualifierInfo
    {
        internal Oid PolicyQualifierId { get; }
        internal ReadOnlyMemory<byte>? Qualifier { get; }

        internal PolicyQualifierInfo(Oid policyQualifierId, ReadOnlyMemory<byte>? qualifier)
        {
            ArgumentNullException.ThrowIfNull(policyQualifierId, nameof(policyQualifierId));

            PolicyQualifierId = policyQualifierId;
            Qualifier = qualifier;
        }

        internal static PolicyQualifierInfo Decode(AsnReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader, nameof(reader));

            AsnReader policyQualifierReader = reader.ReadSequence();
            Oid policyQualifierId = new(policyQualifierReader.ReadObjectIdentifier());
            ReadOnlyMemory<byte>? qualifier = null;

            if (policyQualifierReader.HasData)
            {
                qualifier = policyQualifierReader.ReadEncodedValue();
            }

            policyQualifierReader.ThrowIfNotEmpty();

            return new PolicyQualifierInfo(policyQualifierId, qualifier);
        }

        internal ReadOnlyMemory<byte> Encode()
        {
            AsnWriter writer = new(AsnEncodingRules.DER);

            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(PolicyQualifierId.Value!);

                if (Qualifier.HasValue)
                {
                    writer.WriteEncodedValue(Qualifier.Value.Span);
                }
            }

            return writer.Encode();
        }
    }
}