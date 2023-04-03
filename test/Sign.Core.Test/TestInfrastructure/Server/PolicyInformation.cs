#pragma warning disable IDE0073
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Sign.Core.Test
{
    /*
        From RFC 5280 (https://tools.ietf.org/html/rfc5280#appendix-A.2):

            PolicyInformation ::= SEQUENCE {
                policyIdentifier   CertPolicyId,
                policyQualifiers   SEQUENCE SIZE (1..MAX) OF
                                        PolicyQualifierInfo OPTIONAL }

            CertPolicyId ::= OBJECT IDENTIFIER
	*/
    internal sealed class PolicyInformation
    {
        internal Oid PolicyIdentifier { get; }
        internal IReadOnlyList<PolicyQualifierInfo>? PolicyQualifiers { get; }

        internal PolicyInformation(Oid policyIdentifier, IReadOnlyList<PolicyQualifierInfo>? policyQualifiers)
        {
            ArgumentNullException.ThrowIfNull(policyIdentifier, nameof(policyIdentifier));

            PolicyIdentifier = policyIdentifier;
            PolicyQualifiers = policyQualifiers;
        }

        internal static PolicyInformation Decode(AsnReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader, nameof(reader));

            AsnReader policyInfoReader = reader.ReadSequence();
            Oid policyIdentifier = new(policyInfoReader.ReadObjectIdentifier());
            bool isAnyPolicy = policyIdentifier.IsEqualTo(Oids.AnyPolicy);
            IReadOnlyList<PolicyQualifierInfo>? policyQualifiers = null;

            if (policyInfoReader.HasData)
            {
                policyQualifiers = ReadPolicyQualifiers(policyInfoReader, isAnyPolicy);
            }

            policyInfoReader.ThrowIfNotEmpty();

            return new PolicyInformation(policyIdentifier, policyQualifiers);
        }

        internal ReadOnlyMemory<byte> Encode()
        {
            AsnWriter writer = new(AsnEncodingRules.DER);

            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(PolicyIdentifier.Value!);

                if (PolicyQualifiers is not null)
                {
                    using (writer.PushSequence())
                    {
                        foreach (PolicyQualifierInfo policyQualifier in PolicyQualifiers)
                        {
                            ReadOnlyMemory<byte> bytes = policyQualifier.Encode();

                            writer.WriteEncodedValue(bytes.Span);
                        }
                    }
                }
            }

            return writer.Encode();
        }

        private static IReadOnlyList<PolicyQualifierInfo> ReadPolicyQualifiers(AsnReader reader, bool isAnyPolicy)
        {
            AsnReader policyQualifiersReader = reader.ReadSequence();
            List<PolicyQualifierInfo> policyQualifiers = new();

            while (policyQualifiersReader.HasData)
            {
                PolicyQualifierInfo policyQualifier = PolicyQualifierInfo.Decode(policyQualifiersReader);

                if (isAnyPolicy)
                {
                    if (!policyQualifier.PolicyQualifierId.IsEqualTo(Oids.IdQtCps) &&
                        !policyQualifier.PolicyQualifierId.IsEqualTo(Oids.IdQtUnotice))
                    {
                        throw new AsnContentException();
                    }
                }

                policyQualifiers.Add(policyQualifier);
            }

            if (policyQualifiers.Count == 0)
            {
                throw new AsnContentException();
            }

            return policyQualifiers.AsReadOnly();
        }
    }
}