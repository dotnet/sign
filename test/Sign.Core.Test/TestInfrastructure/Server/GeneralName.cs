#pragma warning disable IDE0073
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core.Test
{
    /*
        From RFC 3280 (https://tools.ietf.org/html/rfc3280#section-4.2.1.7):

            GeneralName ::= CHOICE {
                    otherName                       [0]     OtherName,
                    rfc822Name                      [1]     IA5String,
                    dNSName                         [2]     IA5String,
                    x400Address                     [3]     ORAddress,
                    directoryName                   [4]     Name,
                    ediPartyName                    [5]     EDIPartyName,
                    uniformResourceIdentifier       [6]     IA5String,
                    iPAddress                       [7]     OCTET STRING,
                    registeredID                    [8]     OBJECT IDENTIFIER }

                OtherName ::= SEQUENCE {
                    type-id    OBJECT IDENTIFIER,
                    value      [0] EXPLICIT ANY DEFINED BY type-id }

                EDIPartyName ::= SEQUENCE {
                    nameAssigner            [0]     DirectoryString OPTIONAL,
                    partyName               [1]     DirectoryString }


        From RFC 2459 (https://tools.ietf.org/html/rfc2459.html#section-4.1.2.4):

            Name ::= CHOICE {
                RDNSequence }

            RDNSequence ::= SEQUENCE OF RelativeDistinguishedName

            RelativeDistinguishedName ::=
                SET OF AttributeTypeAndValue

            AttributeTypeAndValue ::= SEQUENCE {
                type     AttributeType,
                value    AttributeValue }

            AttributeType ::= OBJECT IDENTIFIER

            AttributeValue ::= ANY DEFINED BY AttributeType
	*/
    internal sealed class GeneralName
    {
        // Per RFC 2634 section 5.4.1 (https://tools.ietf.org/html/rfc2634#section-5.4.1)
        // only the directory name choice (#4) is allowed.
        private static readonly Asn1Tag Tag = new(TagClass.ContextSpecific, tagValue: 4, isConstructed: true);

        internal X500DistinguishedName DirectoryName { get; }

        internal GeneralName(X500DistinguishedName directoryName)
        {
            ArgumentNullException.ThrowIfNull(directoryName, nameof(directoryName));

            DirectoryName = directoryName;
        }

        internal static GeneralName Decode(AsnReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader, nameof(reader));

            Asn1Tag nextTag = reader.PeekTag();

            if (nextTag.Equals(Tag))
            {
                AsnReader nameReader = reader.ReadSequence(Tag);
                ReadOnlyMemory<byte> bytes = nameReader.ReadEncodedValue();

                nameReader.ThrowIfNotEmpty();

                X500DistinguishedName directoryName = new(bytes.Span);

                return new GeneralName(directoryName);
            }

            throw new AsnContentException();
        }

        internal ReadOnlyMemory<byte> Encode()
        {
            AsnWriter writer = new(AsnEncodingRules.DER);

            using (writer.PushSequence(Tag))
            {
                writer.WriteEncodedValue(DirectoryName.RawData);
            }

            return writer.Encode();
        }
    }
}