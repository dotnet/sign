// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;

namespace Sign.Core.Test
{
    internal static class Oids
    {
        internal static readonly Oid AnyPolicy = new(DottedDecimalValues.AnyPolicy);
        internal static readonly Oid CodeSigningEku = new(DottedDecimalValues.CodeSigningEku);
        internal static readonly Oid IdQtCps = new(DottedDecimalValues.IdQtCps);
        internal static readonly Oid IdQtUnotice = new(DottedDecimalValues.IdQtUnotice);
        internal static readonly Oid MicrosoftRfc3161Timestamp = new(DottedDecimalValues.MicrosoftRfc3161Timestamp);
        internal static readonly Oid Sha1 = new(DottedDecimalValues.Sha1);
        internal static readonly Oid Sha256 = new(DottedDecimalValues.Sha256);
        internal static readonly Oid Sha384 = new(DottedDecimalValues.Sha384);
        internal static readonly Oid Sha512 = new(DottedDecimalValues.Sha512);
        internal static readonly Oid SigningCertificateV2 = new(DottedDecimalValues.SigningCertificateV2);
        internal static readonly Oid Test = new(DottedDecimalValues.Test);
        internal static readonly Oid TestCertPolicyOne = new(DottedDecimalValues.TestCertPolicyOne);
        internal static readonly Oid TimeStampingEku = new(DottedDecimalValues.TimeStampingEku);
        internal static readonly Oid TSTInfoContentType = new(DottedDecimalValues.TSTInfoContentType);

        private static class DottedDecimalValues
        {
            // RFC 5280 "anyPolicy" (https://datatracker.ietf.org/doc/html/rfc5280#section-4.2.1.4)
            internal const string AnyPolicy = "2.5.29.32.0";

            // RFC 5280 codeSigning attribute, (https://datatracker.ietf.org/doc/html/rfc5280#section-4.2.1.12)
            internal const string CodeSigningEku = "1.3.6.1.5.5.7.3.3";

            // RFC 5280 "id-qt-cps" (https://datatracker.ietf.org/doc/html/rfc5280#section-4.2.1.4)
            internal const string IdQtCps = "1.3.6.1.5.5.7.2.1";

            // RFC 5280 "id-qt-unotice" (https://datatracker.ietf.org/doc/html/rfc5280#section-4.2.1.4)
            internal const string IdQtUnotice = "1.3.6.1.5.5.7.2.2";

            // "szOID_RFC3161_counterSign" from wincrypt.h
            internal const string MicrosoftRfc3161Timestamp = "1.3.6.1.4.1.311.3.3.1";

            // RFC 8017 appendix B.1 (https://datatracker.ietf.org/doc/html/rfc8017#appendix-B.1)
            internal const string Sha1 = "1.3.14.3.2.26";

            // RFC 8017 appendix B.1 (https://datatracker.ietf.org/doc/html/rfc8017#appendix-B.1)
            internal const string Sha256 = "2.16.840.1.101.3.4.2.1";

            // RFC 8017 appendix B.1 (https://datatracker.ietf.org/doc/html/rfc8017#appendix-B.1)
            internal const string Sha384 = "2.16.840.1.101.3.4.2.2";

            // RFC 8017 appendix B.1 (https://datatracker.ietf.org/doc/html/rfc8017#appendix-B.1)
            internal const string Sha512 = "2.16.840.1.101.3.4.2.3";

            // RFC 5126 "signing-certificate-v2" (https://datatracker.ietf.org/doc/html/rfc5126.html#page-34)
            internal const string SigningCertificateV2 = "1.2.840.113549.1.9.16.2.47";

            // RFC 7229 "id-TEST" https://datatracker.ietf.org/doc/html/rfc7229#section-2
            internal const string Test = "1.3.6.1.5.5.7.13";

            // RFC 7229 "id-TEST-certPolicyOne" https://datatracker.ietf.org/doc/html/rfc7229#section-2
            internal const string TestCertPolicyOne = "1.3.6.1.5.5.7.13.1";

            // RFC 3280 "id-kp-timeStamping" (https://datatracker.ietf.org/doc/html/rfc3280.html#section-4.2.1.13)
            internal const string TimeStampingEku = "1.3.6.1.5.5.7.3.8";

            // RFC 3161 "id-ct-TSTInfo" https://datatracker.ietf.org/doc/html/rfc3161#section-2.4.2
            internal const string TSTInfoContentType = "1.2.840.113549.1.9.16.1.4";
        }
    }
}