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
            public static readonly Uri rsaMD5 = new Uri("http://www.w3.org/2001/04/xmldsig-more#rsa-md5", UriKind.Absolute);

            public static readonly Uri rsaSHA1 = new Uri("http://www.w3.org/2000/09/xmldsig#rsa-sha1", UriKind.Absolute);
            public static readonly Uri rsaSHA256 = new Uri("http://www.w3.org/2001/04/xmldsig-more#rsa-sha256", UriKind.Absolute);
            public static readonly Uri rsaSHA384 = new Uri("http://www.w3.org/2001/04/xmldsig-more#rsa-sha384", UriKind.Absolute);
            public static readonly Uri rsaSHA512 = new Uri("http://www.w3.org/2001/04/xmldsig-more#rsa-sha512", UriKind.Absolute);

            public static readonly Uri ecdsaSHA1 = new Uri("http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha1", UriKind.Absolute);
            public static readonly Uri ecdsaSHA256 = new Uri("http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256", UriKind.Absolute);
            public static readonly Uri ecdsaSHA384 = new Uri("http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha384", UriKind.Absolute);
            public static readonly Uri ecdsaSHA512 = new Uri("http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha512", UriKind.Absolute);
        }

        public static class HashAlgorithms
        {
            //These are documented here. https://www.iana.org/assignments/xml-security-uris/xml-security-uris.xhtml
            public static readonly Uri md5DigestUri = new Uri("http://www.w3.org/2001/04/xmldsig-more#md5", UriKind.Absolute);
            public static readonly Uri sha1DigestUri = new Uri("http://www.w3.org/2000/09/xmldsig#sha1", UriKind.Absolute);
            public static readonly Uri sha224DigestUri = new Uri("http://www.w3.org/2001/04/xmldsig-more#sha224", UriKind.Absolute);
            public static readonly Uri sha256DigestUri = new Uri("http://www.w3.org/2001/04/xmlenc#sha256", UriKind.Absolute);
            public static readonly Uri sha384DigestUri = new Uri("http://www.w3.org/2001/04/xmldsig-more#sha384", UriKind.Absolute);
            public static readonly Uri sha512DigestUri = new Uri("http://www.w3.org/2001/04/xmlenc#sha512", UriKind.Absolute);
        }
    }
}
