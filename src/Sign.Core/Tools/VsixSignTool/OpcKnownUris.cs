// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal static class OpcKnownUris
    {
        public static readonly Uri DigitalSignatureOrigin = new Uri("http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/origin", UriKind.Absolute);
        public static readonly Uri DigitalSignatureSignature = new Uri("http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/signature", UriKind.Absolute);

        public static readonly Uri XmlDSig = new Uri("http://www.w3.org/2000/09/xmldsig#", UriKind.Absolute);
        public static readonly Uri XmlDigitalSignature = new Uri("http://schemas.openxmlformats.org/package/2006/digital-signature", UriKind.Absolute);
        public static readonly Uri XmlDSigObject = new Uri("http://www.w3.org/2000/09/xmldsig#Object", UriKind.Absolute);

        public static class SignatureAlgorithms
        {
            public static readonly Uri RsaSHA256 = new Uri("http://www.w3.org/2001/04/xmldsig-more#rsa-sha256", UriKind.Absolute);
            public static readonly Uri RsaSHA384 = new Uri("http://www.w3.org/2001/04/xmldsig-more#rsa-sha384", UriKind.Absolute);
            public static readonly Uri RsaSHA512 = new Uri("http://www.w3.org/2001/04/xmldsig-more#rsa-sha512", UriKind.Absolute);
        }

        public static class HashAlgorithms
        {
            //These are documented here. https://www.iana.org/assignments/xml-security-uris/xml-security-uris.xhtml
            public static readonly Uri Sha256DigestUri = new Uri("http://www.w3.org/2001/04/xmlenc#sha256", UriKind.Absolute);
            public static readonly Uri Sha384DigestUri = new Uri("http://www.w3.org/2001/04/xmldsig-more#sha384", UriKind.Absolute);
            public static readonly Uri Sha512DigestUri = new Uri("http://www.w3.org/2001/04/xmlenc#sha512", UriKind.Absolute);
        }
    }
}
