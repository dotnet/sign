#pragma warning disable IDE0073
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Sign.Core.Test
{
    /*
        From RFC 5126 (https://tools.ietf.org/html/rfc5126.html#section-5.11.1):

            CommitmentTypeQualifier ::= SEQUENCE {
               commitmentTypeIdentifier   CommitmentTypeIdentifier,
               qualifier                  ANY DEFINED BY commitmentTypeIdentifier }

            CommitmentTypeIdentifier ::= OBJECT IDENTIFIER
	*/
    internal sealed class CommitmentTypeQualifier
    {
        internal Oid CommitmentTypeIdentifier { get; }
        internal ReadOnlyMemory<byte>? Qualifier { get; }

        internal CommitmentTypeQualifier(
            Oid commitmentTypeIdentifier,
            ReadOnlyMemory<byte>? qualifier = null)
        {
            ArgumentNullException.ThrowIfNull(commitmentTypeIdentifier, nameof(commitmentTypeIdentifier));

            CommitmentTypeIdentifier = commitmentTypeIdentifier;
            Qualifier = qualifier;
        }

        internal static CommitmentTypeQualifier Decode(AsnReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader, nameof(reader));

            AsnReader commitmentTypeQualifierReader = reader.ReadSequence();
            Oid commitmentTypeIdentifier = new(commitmentTypeQualifierReader.ReadObjectIdentifier());
            ReadOnlyMemory<byte>? qualifier = null;

            if (commitmentTypeQualifierReader.HasData)
            {
                qualifier = commitmentTypeQualifierReader.ReadEncodedValue();
            }

            commitmentTypeQualifierReader.ThrowIfNotEmpty();

            return new CommitmentTypeQualifier(commitmentTypeIdentifier, qualifier);
        }

        internal ReadOnlyMemory<byte> Encode()
        {
            AsnWriter writer = new(AsnEncodingRules.DER);

            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(CommitmentTypeIdentifier.Value!);

                if (Qualifier.HasValue)
                {
                    writer.WriteEncodedValue(Qualifier.Value.Span);
                }
            }

            return writer.Encode();
        }
    }
}