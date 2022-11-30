#pragma warning disable IDE0073 // The file header does not match the required text
// ==++==
// 
//   Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// ==--==

//
// The MIT License (MIT)
//
// Copyright (c) Microsoft Corporation
//
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions: 
//
// The above copyright notice and this permission notice shall be included in all 
// copies or substantial portions of the Software. 
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE 
// SOFTWARE.
//

// From https://github.com/Microsoft/referencesource/blob/7de0d30c7c5ef56ab60fee41fcdb50005d24979a/inc/mansign2.cs

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

using RSAKeyVaultProvider;

#pragma warning disable CS8600
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8618
#pragma warning disable CS8625

#pragma warning disable CA1822
#pragma warning disable CA1416 // Call site unreachable on all platforms

#pragma warning disable IDE0016
#pragma warning disable IDE0017
#pragma warning disable IDE0019
#pragma warning disable IDE0029
#pragma warning disable IDE0031
#pragma warning disable IDE0032
#pragma warning disable IDE0049
#pragma warning disable IDE0074
#pragma warning disable IDE1006 // Naming Styles

namespace System.Deployment.Internal.CodeSigning
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct BLOBHEADER
    {
        internal byte bType;
        internal byte bVersion;
        internal short reserved;
        internal uint aiKeyAlg;
    };

    class ManifestSignedXml2 : SignedXml
    {
        const string Sha256SignatureMethodUri = @"http://www.w3.org/2000/09/xmldsig#rsa-sha256";
        const string Sha256DigestMethod = @"http://www.w3.org/2000/09/xmldsig#sha256";

        internal ManifestSignedXml2()
            : base()
        {
            init();
        }
        internal ManifestSignedXml2(XmlElement elem)
            : base(elem)
        {
            init();
        }
        internal ManifestSignedXml2(XmlDocument document)
            : base(document)
        {
            init();
        }

        void init()
        {
            CryptoConfig.AddAlgorithm(typeof(Sign.Core.RSAPKCS1SHA256SignatureDescription),
                               Sha256SignatureMethodUri);

#pragma warning disable SYSLIB0021
            CryptoConfig.AddAlgorithm(typeof(System.Security.Cryptography.SHA256Managed),
                               Sha256DigestMethod);
#pragma warning restore SYSLIB0021
        }
    }

    class SignedCmiManifest2
    {
        readonly XmlDocument m_manifestDom = null;

        const string Sha256SignatureMethodUri = @"http://www.w3.org/2000/09/xmldsig#rsa-sha256";
        const string Sha256DigestMethod = @"http://www.w3.org/2000/09/xmldsig#sha256";

        const string wintrustPolicyFlagsRegPath = "Software\\Microsoft\\Windows\\CurrentVersion\\WinTrust\\Trust Providers\\Software Publishing";
        const string wintrustPolicyFlagsRegName = "State";

        SignedCmiManifest2() { }

        internal SignedCmiManifest2(XmlDocument manifestDom)
        {
            if (manifestDom == null)
            {
                throw new ArgumentNullException(nameof(manifestDom));
            }

            m_manifestDom = manifestDom;
        }

        internal void Sign(CmiManifestSigner2 signer)
        {
            Sign(signer, null);
        }

        internal void Sign(CmiManifestSigner2 signer, string timeStampUrl)
        {
            // Signer cannot be null.
            if (signer == null || signer.StrongNameKey == null)
            {
                throw new ArgumentNullException(nameof(signer));
            }

            // Remove existing SN signature.
            RemoveExistingSignature(m_manifestDom);

            // Replace public key token in assemblyIdentity if requested.
            if ((signer.Flag & CmiManifestSignerFlag.DontReplacePublicKeyToken) == 0)
            {
                ReplacePublicKeyToken(m_manifestDom, signer.StrongNameKey);
            }

            // No cert means don't Authenticode sign and timestamp.
            XmlDocument licenseDom = null;
            if (signer.Certificate != null)
            {
                // Yes. We will Authenticode sign, so first insert <publisherIdentity />
                // element, if necessary.
                InsertPublisherIdentity(m_manifestDom, signer.Certificate);

                // Now create the license DOM, and then sign it.
                licenseDom = CreateLicenseDom(signer, ExtractPrincipalFromManifest(), ComputeHashFromManifest(m_manifestDom));
                AuthenticodeSignLicenseDom(licenseDom, signer, timeStampUrl);
            }

            StrongNameSignManifestDom(m_manifestDom, licenseDom, signer);
        }

        XmlElement ExtractPrincipalFromManifest()
        {
            var nsm = new XmlNamespaceManager(m_manifestDom.NameTable);
            nsm.AddNamespace("asm", AssemblyNamespaceUri);
            var assemblyIdentityNode = m_manifestDom.SelectSingleNode("asm:assembly/asm:assemblyIdentity", nsm);
            if (assemblyIdentityNode == null)
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            return assemblyIdentityNode as XmlElement;
        }

        //
        // Statics.
        //
        static void InsertPublisherIdentity(XmlDocument manifestDom, X509Certificate2 signerCert)
        {
            var nsm = new XmlNamespaceManager(manifestDom.NameTable);
            nsm.AddNamespace("asm", AssemblyNamespaceUri);
            nsm.AddNamespace("asm2", AssemblyV2NamespaceUri);
            nsm.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

            var assembly = manifestDom.SelectSingleNode("asm:assembly", nsm) as XmlElement;
            var assemblyIdentity = manifestDom.SelectSingleNode("asm:assembly/asm:assemblyIdentity", nsm) as XmlElement;
            if (assemblyIdentity == null)
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            // Reuse existing node if exists
            var publisherIdentity = manifestDom.SelectSingleNode("asm:assembly/asm2:publisherIdentity", nsm) as XmlElement;
            if (publisherIdentity == null)
            {
                // create new if not exist
                publisherIdentity = manifestDom.CreateElement("publisherIdentity", AssemblyV2NamespaceUri);
            }
            // Get the issuer's public key blob hash.
            var pIssuerKeyHash = new IntPtr();
            var hr = Win32._AxlGetIssuerPublicKeyHash(signerCert.Handle, ref pIssuerKeyHash);
            if (hr != Win32.S_OK)
            {
                throw new CryptographicException(hr);
            }

            var issuerKeyHash = Marshal.PtrToStringUni(pIssuerKeyHash);
            Win32.HeapFree(Win32.GetProcessHeap(), 0, pIssuerKeyHash);

            publisherIdentity.SetAttribute("name", signerCert.SubjectName.Name);
            publisherIdentity.SetAttribute("issuerKeyHash", issuerKeyHash);

            var signature = manifestDom.SelectSingleNode("asm:assembly/ds:Signature", nsm) as XmlElement;
            if (signature != null)
            {
                assembly.InsertBefore(publisherIdentity, signature);
            }
            else
            {
                assembly.AppendChild(publisherIdentity);
            }
        }

        static void RemoveExistingSignature(XmlDocument manifestDom)
        {
            var nsm = new XmlNamespaceManager(manifestDom.NameTable);
            nsm.AddNamespace("asm", AssemblyNamespaceUri);
            nsm.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            var signatureNode = manifestDom.SelectSingleNode("asm:assembly/ds:Signature", nsm);
            if (signatureNode != null)
            {
                signatureNode.ParentNode.RemoveChild(signatureNode);
            }
        }

        /// <summary>
        /// The reason you need provider type 24, is because that’s the only RSA provider type that supports SHA-2 operations.   (For instance, PROV_RSA_FULL does not support SHA-2).
        /// As for official guidance – I’m not sure of any.    For workarounds though, if you’re using the Microsoft software CSPs, they share the underlying key store.  You can get the key container name from your RSA object, then open up a new RSA object with the same key container name but with PROV_RSA_AES.   At that point, you should be able to use SHA-2 algorithms.
        /// </summary>
        /// <param name="oldCsp"></param>
        /// <returns></returns>
        internal static RSACryptoServiceProvider GetFixedRSACryptoServiceProvider(RSACryptoServiceProvider oldCsp)
        {
            // 3rd party crypto providers in general don't need to be forcefully upgraded.
            // This is not an ideal way to check for that but is the best we have available.
            if (!oldCsp.CspKeyContainerInfo.ProviderName.StartsWith("Microsoft", StringComparison.Ordinal))
            {
                return oldCsp;
            }

            const int PROV_RSA_AES = 24;    // CryptoApi provider type for an RSA provider supporting sha-256 digital signatures
            var csp = new CspParameters();
            csp.ProviderType = PROV_RSA_AES;
            csp.KeyContainerName = oldCsp.CspKeyContainerInfo.KeyContainerName;
            csp.KeyNumber = (int)oldCsp.CspKeyContainerInfo.KeyNumber;
            if (oldCsp.CspKeyContainerInfo.MachineKeyStore)
            {
                csp.Flags = CspProviderFlags.UseMachineKeyStore;
            }

            var fixedRsa = new RSACryptoServiceProvider(csp);

            return fixedRsa;

        }

        static void ReplacePublicKeyToken(XmlDocument manifestDom, AsymmetricAlgorithm snKey)
        {
            // Make sure we can find the publicKeyToken attribute.
            var nsm = new XmlNamespaceManager(manifestDom.NameTable);
            nsm.AddNamespace("asm", AssemblyNamespaceUri);
            var assemblyIdentity = manifestDom.SelectSingleNode("asm:assembly/asm:assemblyIdentity", nsm) as XmlElement;
            if (assemblyIdentity == null)
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            if (!assemblyIdentity.HasAttribute("publicKeyToken"))
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            byte[] cspPublicKeyBlob;

            if (snKey is RSACryptoServiceProvider provider)
            {
                cspPublicKeyBlob = (GetFixedRSACryptoServiceProvider(provider)).ExportCspBlob(false);
                if (cspPublicKeyBlob == null || cspPublicKeyBlob.Length == 0)
                {
                    throw new CryptographicException(Win32.NTE_BAD_KEY);
                }
            }
            else
            {
                using var rsaCsp = new RSACryptoServiceProvider();
                rsaCsp.ImportParameters(((RSA)snKey).ExportParameters(false));
                cspPublicKeyBlob = rsaCsp.ExportCspBlob(false);
            }

            // Now compute the public key token.
            unsafe
            {
                fixed (byte* pbPublicKeyBlob = cspPublicKeyBlob)
                {
                    var publicKeyBlob = new Win32.CRYPT_DATA_BLOB();
                    publicKeyBlob.cbData = (uint)cspPublicKeyBlob.Length;
                    publicKeyBlob.pbData = new IntPtr(pbPublicKeyBlob);
                    var pPublicKeyToken = new IntPtr();

                    var hr = Win32._AxlPublicKeyBlobToPublicKeyToken(ref publicKeyBlob, ref pPublicKeyToken);
                    if (hr != Win32.S_OK)
                    {
                        throw new CryptographicException(hr);
                    }

                    var publicKeyToken = Marshal.PtrToStringUni(pPublicKeyToken);
                    Win32.HeapFree(Win32.GetProcessHeap(), 0, pPublicKeyToken);

                    assemblyIdentity.SetAttribute("publicKeyToken", publicKeyToken);
                }
            }
        }

        static string GetPublicKeyToken(XmlDocument manifestDom)
        {
            var nsm = new XmlNamespaceManager(manifestDom.NameTable);
            nsm.AddNamespace("asm", AssemblyNamespaceUri);
            nsm.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

            var assemblyIdentity = manifestDom.SelectSingleNode("asm:assembly/asm:assemblyIdentity", nsm) as XmlElement;

            if (assemblyIdentity == null || !assemblyIdentity.HasAttribute("publicKeyToken"))
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            return assemblyIdentity.GetAttribute("publicKeyToken");
        }

        static byte[] ComputeHashFromManifest(XmlDocument manifestDom)
        {
#if (true) // 
            return ComputeHashFromManifest(manifestDom, false);
        }

        static byte[] ComputeHashFromManifest(XmlDocument manifestDom, bool oldFormat)
        {
            if (oldFormat)
            {
                var exc = new XmlDsigExcC14NTransform();
                exc.LoadInput(manifestDom);

                using var sha2 = SHA256.Create();
                var hash = sha2.ComputeHash(exc.GetOutput() as MemoryStream);
                if (hash == null)
                {
                    throw new CryptographicException(Win32.TRUST_E_BAD_DIGEST);
                }

                return hash;
            }
            else
            {
#endif
                // Since the DOM given to us is not guaranteed to be normalized,
                // we need to normalize it ourselves. Also, we always preserve
                // white space as Fusion XML engine always preserve white space.
                var normalizedDom = new XmlDocument();
                normalizedDom.PreserveWhitespace = true;

                // Normalize the document
                using (TextReader stringReader = new StringReader(manifestDom.OuterXml))
                {
                    var settings = new XmlReaderSettings();
                    settings.DtdProcessing = DtdProcessing.Parse;
                    var reader = XmlReader.Create(stringReader, settings, manifestDom.BaseURI);
                    normalizedDom.Load(reader);
                }

                var exc = new XmlDsigExcC14NTransform();
                exc.LoadInput(normalizedDom);

                using var sha2 = SHA256.Create();
                var hash = sha2.ComputeHash(exc.GetOutput() as MemoryStream);
                if (hash == null)
                {
                    throw new CryptographicException(Win32.TRUST_E_BAD_DIGEST);
                }

                return hash;

#if (true) // 
            }
#endif
        }

        const string AssemblyNamespaceUri = "urn:schemas-microsoft-com:asm.v1";
        const string AssemblyV2NamespaceUri = "urn:schemas-microsoft-com:asm.v2";
        const string MSRelNamespaceUri = "http://schemas.microsoft.com/windows/rel/2005/reldata";
        const string LicenseNamespaceUri = "urn:mpeg:mpeg21:2003:01-REL-R-NS";
        const string AuthenticodeNamespaceUri = "http://schemas.microsoft.com/windows/pki/2005/Authenticode";
        const string licenseTemplate = "<r:license xmlns:r=\"" + LicenseNamespaceUri + "\" xmlns:as=\"" + AuthenticodeNamespaceUri + "\">" +
                                                    @"<r:grant>" +
                                                    @"<as:ManifestInformation>" +
                                                    @"<as:assemblyIdentity />" +
                                                    @"</as:ManifestInformation>" +
                                                    @"<as:SignedBy/>" +
                                                    @"<as:AuthenticodePublisher>" +
                                                    @"<as:X509SubjectName>CN=dummy</as:X509SubjectName>" +
                                                    @"</as:AuthenticodePublisher>" +
                                                    @"</r:grant><r:issuer></r:issuer></r:license>";

        static XmlDocument CreateLicenseDom(CmiManifestSigner2 signer, XmlElement principal, byte[] hash)
        {
            var licenseDom = new XmlDocument();
            licenseDom.PreserveWhitespace = true;
            licenseDom.LoadXml(licenseTemplate);
            var nsm = new XmlNamespaceManager(licenseDom.NameTable);
            nsm.AddNamespace("r", LicenseNamespaceUri);
            nsm.AddNamespace("as", AuthenticodeNamespaceUri);
            var assemblyIdentityNode = licenseDom.SelectSingleNode("r:license/r:grant/as:ManifestInformation/as:assemblyIdentity", nsm) as XmlElement;
            assemblyIdentityNode.RemoveAllAttributes();
            foreach (XmlAttribute attribute in principal.Attributes)
            {
                assemblyIdentityNode.SetAttribute(attribute.Name, attribute.Value);
            }

            var manifestInformationNode = licenseDom.SelectSingleNode("r:license/r:grant/as:ManifestInformation", nsm) as XmlElement;

            manifestInformationNode.SetAttribute("Hash", hash.Length == 0 ? "" : BytesToHexString(hash, 0, hash.Length));
            manifestInformationNode.SetAttribute("Description", signer.Description == null ? "" : signer.Description);
            manifestInformationNode.SetAttribute("Url", signer.DescriptionUrl == null ? "" : signer.DescriptionUrl);

            var authenticodePublisherNode = licenseDom.SelectSingleNode("r:license/r:grant/as:AuthenticodePublisher/as:X509SubjectName", nsm) as XmlElement;
            authenticodePublisherNode.InnerText = signer.Certificate.SubjectName.Name;

            return licenseDom;
        }

        static void AuthenticodeSignLicenseDom(XmlDocument licenseDom, CmiManifestSigner2 signer, string timeStampUrl)
        {
            // Make sure it is RSA, as this is the only one Fusion will support.
            // HACK: do this in a better way
            RSA rsaPrivateKey = null;
            if (signer.Certificate.HasPrivateKey)
            {
                rsaPrivateKey = signer.Certificate.GetRSAPrivateKey();
            }
            else if (signer.StrongNameKey is RSAKeyVault provider)
            {
                rsaPrivateKey = provider;
            }

            try
            {
                if (rsaPrivateKey == null)
                {
                    throw new NotSupportedException();
                }

                // Setup up XMLDSIG engine.
                var signedXml = new ManifestSignedXml2(licenseDom);
                signedXml.SigningKey = rsaPrivateKey;
                signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
                signedXml.SignedInfo.SignatureMethod = Sha256SignatureMethodUri;

                // Add the key information.
                signedXml.KeyInfo.AddClause(new RSAKeyValue(rsaPrivateKey));
                signedXml.KeyInfo.AddClause(new KeyInfoX509Data(signer.Certificate, signer.IncludeOption));

                // Add the enveloped reference.
                var reference = new Reference();
                reference.Uri = "";
                reference.DigestMethod = Sha256DigestMethod;

                // Add an enveloped and an Exc-C14N transform.
                reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
#if (false) // 
            reference.AddTransform(new XmlLicenseTransform()); 
#endif
                reference.AddTransform(new XmlDsigExcC14NTransform());

                // Add the reference.
                signedXml.AddReference(reference);

                // Compute the signature.
                signedXml.ComputeSignature();

                // Get the XML representation
                var xmlDigitalSignature = signedXml.GetXml();
                xmlDigitalSignature.SetAttribute("Id", "AuthenticodeSignature");

                // Insert the signature node under the issuer element.
                var nsm = new XmlNamespaceManager(licenseDom.NameTable);
                nsm.AddNamespace("r", LicenseNamespaceUri);
                var issuerNode = licenseDom.SelectSingleNode("r:license/r:issuer", nsm) as XmlElement;
                issuerNode.AppendChild(licenseDom.ImportNode(xmlDigitalSignature, true));

                // Time stamp it if requested.
                if (timeStampUrl != null && timeStampUrl.Length != 0)
                {
                    TimestampSignedLicenseDom(licenseDom, timeStampUrl);
                }

                // Wrap it inside a RelData element.
                licenseDom.DocumentElement.ParentNode.InnerXml = "<msrel:RelData xmlns:msrel=\"" +
                                                                 MSRelNamespaceUri + "\">" +
                                                                 licenseDom.OuterXml + "</msrel:RelData>";
            }
            finally
            {
                if (rsaPrivateKey != signer.StrongNameKey)
                {
                    rsaPrivateKey.Dispose();
                }
            }
        }

        static void TimestampSignedLicenseDom(XmlDocument licenseDom, string timeStampUrl)
        {
            var timestampBlob = new Win32.CRYPT_DATA_BLOB();

            var nsm = new XmlNamespaceManager(licenseDom.NameTable);
            nsm.AddNamespace("r", LicenseNamespaceUri);
            nsm.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            nsm.AddNamespace("as", AuthenticodeNamespaceUri);

            var licenseXml = Encoding.UTF8.GetBytes(licenseDom.OuterXml);

            unsafe
            {
                fixed (byte* pbLicense = licenseXml)
                {
                    var licenseBlob = new Win32.CRYPT_DATA_BLOB();
                    var pvLicense = new IntPtr(pbLicense);
                    licenseBlob.cbData = (uint)licenseXml.Length;
                    licenseBlob.pbData = pvLicense;

                    var hr = Win32.CertTimestampAuthenticodeLicense(ref licenseBlob, timeStampUrl, ref timestampBlob);
                    if (hr != Win32.S_OK)
                    {
                        throw new CryptographicException(hr);
                    }
                }
            }

            var timestampSignature = new byte[timestampBlob.cbData];
            Marshal.Copy(timestampBlob.pbData, timestampSignature, 0, timestampSignature.Length);
            Win32.HeapFree(Win32.GetProcessHeap(), 0, timestampBlob.pbData);

            var asTimestamp = licenseDom.CreateElement("as", "Timestamp", AuthenticodeNamespaceUri);
            asTimestamp.InnerText = Encoding.UTF8.GetString(timestampSignature);

            var dsObject = licenseDom.CreateElement("Object", SignedXml.XmlDsigNamespaceUrl);
            dsObject.AppendChild(asTimestamp);

            var signatureNode = licenseDom.SelectSingleNode("r:license/r:issuer/ds:Signature", nsm) as XmlElement;
            signatureNode.AppendChild(dsObject);
        }

        static void StrongNameSignManifestDom(XmlDocument manifestDom, XmlDocument licenseDom, CmiManifestSigner2 signer)
        {
            var snKey = signer.StrongNameKey as RSA;

            // Make sure it is RSA, as this is the only one Fusion will support.
            if (snKey == null)
            {
                throw new NotSupportedException();
            }

            // Setup namespace manager.
            var nsm = new XmlNamespaceManager(manifestDom.NameTable);
            nsm.AddNamespace("asm", AssemblyNamespaceUri);

            // Get to root element.
            var signatureParent = manifestDom.SelectSingleNode("asm:assembly", nsm) as XmlElement;
            if (signatureParent == null)
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            if (signer.StrongNameKey is not RSA)
            {
                throw new NotSupportedException();
            }

            // Setup up XMLDSIG engine.
            var signedXml = new ManifestSignedXml2(signatureParent);
            if (signer.StrongNameKey is RSACryptoServiceProvider)
            {
                signedXml.SigningKey = GetFixedRSACryptoServiceProvider(signer.StrongNameKey as RSACryptoServiceProvider);
            }
            else
            {
                signedXml.SigningKey = signer.StrongNameKey;
            }

            signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
            signedXml.SignedInfo.SignatureMethod = Sha256SignatureMethodUri;

            // Add the key information.
            signedXml.KeyInfo.AddClause(new RSAKeyValue(snKey));
            if (licenseDom != null)
            {
                signedXml.KeyInfo.AddClause(new KeyInfoNode(licenseDom.DocumentElement));
            }

            signedXml.KeyInfo.Id = "StrongNameKeyInfo";

            // Add the enveloped reference.
            var enveloped = new Reference();
            enveloped.Uri = "";
            enveloped.DigestMethod = Sha256DigestMethod;

            // Add an enveloped then Exc-C14N transform.
            enveloped.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            enveloped.AddTransform(new XmlDsigExcC14NTransform());
            signedXml.AddReference(enveloped);

#if (false) // DSIE: New format does not sign KeyInfo.
            // Add the key info reference.
            Reference strongNameKeyInfo = new Reference();
            strongNameKeyInfo.Uri = "#StrongNameKeyInfo";
            strongNameKeyInfo.AddTransform(new XmlDsigExcC14NTransform());
            signedXml.AddReference(strongNameKeyInfo);
#endif
            // Compute the signature.
            signedXml.ComputeSignature();

            // Get the XML representation
            var xmlDigitalSignature = signedXml.GetXml();
            xmlDigitalSignature.SetAttribute("Id", "StrongNameSignature");

            // Insert the signature now.
            signatureParent.AppendChild(xmlDigitalSignature);
        }
        static readonly char[] hexValues = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };

        static string BytesToHexString(byte[] array, int start, int end)
        {
            string result = null;
            if (array != null)
            {
                var hexOrder = new char[(end - start) * 2];
                var i = end;
                int digit, j = 0;
                while (i-- > start)
                {
                    digit = (array[i] & 0xf0) >> 4;
                    hexOrder[j++] = hexValues[digit];
                    digit = (array[i] & 0x0f);
                    hexOrder[j++] = hexValues[digit];
                }

                result = new String(hexOrder);
            }

            return result;
        }
    }

    class CmiManifestSigner2
    {
        readonly AsymmetricAlgorithm m_strongNameKey;
        readonly X509Certificate2 m_certificate;
        string m_description;
        string m_url;
        readonly X509Certificate2Collection m_certificates;
        X509IncludeOption m_includeOption;
        CmiManifestSignerFlag m_signerFlag;

        CmiManifestSigner2() { }

        internal CmiManifestSigner2(AsymmetricAlgorithm strongNameKey) :
            this(strongNameKey, null)
        { }

        internal CmiManifestSigner2(AsymmetricAlgorithm strongNameKey, X509Certificate2 certificate)
        {
            if (strongNameKey == null)
            {
                throw new ArgumentNullException(nameof(strongNameKey));
            }

#if (true) // 
            var rsa = strongNameKey as RSA;
            if (rsa == null)
            {
                throw new ArgumentNullException(nameof(strongNameKey));
            }
#endif
            m_strongNameKey = strongNameKey;
            m_certificate = certificate;
            m_certificates = new X509Certificate2Collection();
            m_includeOption = X509IncludeOption.ExcludeRoot;
            m_signerFlag = CmiManifestSignerFlag.None;
        }

        internal AsymmetricAlgorithm StrongNameKey
        {
            get
            {
                return m_strongNameKey;
            }
        }

        internal X509Certificate2 Certificate
        {
            get
            {
                return m_certificate;
            }
        }

        internal string Description
        {
            get
            {
                return m_description;
            }
            set
            {
                m_description = value;
            }
        }

        internal string DescriptionUrl
        {
            get
            {
                return m_url;
            }
            set
            {
                m_url = value;
            }
        }

        internal X509Certificate2Collection ExtraStore
        {
            get
            {
                return m_certificates;
            }
        }

        internal X509IncludeOption IncludeOption
        {
            get
            {
                return m_includeOption;
            }
            set
            {
                if (value < X509IncludeOption.None || value > X509IncludeOption.WholeChain)
                {
                    throw new ArgumentException(null, nameof(value));
                }

                if (m_includeOption == X509IncludeOption.None)
                {
                    throw new NotSupportedException();
                }

                m_includeOption = value;
            }
        }

        internal CmiManifestSignerFlag Flag
        {
            get
            {
                return m_signerFlag;
            }
            set
            {
                unchecked
                {
                    if ((value & ((CmiManifestSignerFlag)~CimManifestSignerFlagMask)) != 0)
                    {
                        throw new ArgumentException(null, nameof(value));
                    }
                }

                m_signerFlag = value;
            }
        }

        internal const uint CimManifestSignerFlagMask = 0x00000001;
    }
}