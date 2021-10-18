// ==++==
// 
//   Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// ==--==

//
// mansign.cs
//

using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

using RSAKeyVaultProvider;

using _FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

// From: https://github.com/Microsoft/referencesource/blob/7de0d30c7c5ef56ab60fee41fcdb50005d24979a/inc/mansign.cs

#pragma warning disable IDE0003
#pragma warning disable IDE0016
#pragma warning disable IDE0017
#pragma warning disable IDE0018
#pragma warning disable IDE0019
#pragma warning disable IDE0029
#pragma warning disable IDE0032
#pragma warning disable IDE0049
#pragma warning disable IDE0051
#pragma warning disable IDE1006 // Naming Styles
namespace System.Deployment.Internal.CodeSigning
{

    static class Win32
    {
        //
        // PInvoke dll's.
        //
        internal const String KERNEL32 = "kernel32.dll";
#if (true)

#if FEATURE_MAIN_CLR_MODULE_USES_CORE_NAME
        internal const String MSCORWKS = "coreclr.dll";
#elif USE_OLD_MSCORWKS_NAME // for updating devdiv toolset until it has clr.dll
        internal const String MSCORWKS = "mscorwks.dll";
#else //FEATURE_MAIN_CLR_MODULE_USES_CORE_NAME
        internal const String MSCORWKS = "clr.dll";
#endif //FEATURE_MAIN_CLR_MODULE_USES_CORE_NAME

#else
        internal const String MSCORWKS = "isowhidbey.dll";
#endif
        //
        // Constants.
        //
        internal const int S_OK = unchecked(0x00000000);
        internal const int NTE_BAD_KEY = unchecked((int)0x80090003);

        // Trust errors.
        internal const int TRUST_E_SYSTEM_ERROR = unchecked((int)0x80096001);
        internal const int TRUST_E_NO_SIGNER_CERT = unchecked((int)0x80096002);
        internal const int TRUST_E_COUNTER_SIGNER = unchecked((int)0x80096003);
        internal const int TRUST_E_CERT_SIGNATURE = unchecked((int)0x80096004);
        internal const int TRUST_E_TIME_STAMP = unchecked((int)0x80096005);
        internal const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
        internal const int TRUST_E_BASIC_CONSTRAINTS = unchecked((int)0x80096019);
        internal const int TRUST_E_FINANCIAL_CRITERIA = unchecked((int)0x8009601E);
        internal const int TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);
        internal const int TRUST_E_ACTION_UNKNOWN = unchecked((int)0x800B0002);
        internal const int TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003);
        internal const int TRUST_E_SUBJECT_NOT_TRUSTED = unchecked((int)0x800B0004);
        internal const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
        internal const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
        internal const int TRUST_E_FAIL = unchecked((int)0x800B010B);
        internal const int TRUST_E_EXPLICIT_DISTRUST = unchecked((int)0x800B0111);
        internal const int CERT_E_CHAINING = unchecked((int)0x800B010A);


        // Values for dwFlags of CertVerifyAuthenticodeLicense.
        internal const int AXL_REVOCATION_NO_CHECK = unchecked(0x00000001);
        internal const int AXL_REVOCATION_CHECK_END_CERT_ONLY = unchecked(0x00000002);
        internal const int AXL_REVOCATION_CHECK_ENTIRE_CHAIN = unchecked(0x00000004);
        internal const int AXL_URL_CACHE_ONLY_RETRIEVAL = unchecked(0x00000008);
        internal const int AXL_LIFETIME_SIGNING = unchecked(0x00000010);
        internal const int AXL_TRUST_MICROSOFT_ROOT_ONLY = unchecked(0x00000020);

        // Wintrust Policy Flag
        //  These are set during install and can be modified by the user
        //  through various means.  The SETREG.EXE utility (found in the Authenticode
        //  Tools Pack) will select/deselect each of them.
        internal const int WTPF_IGNOREREVOKATION = 0x00000200;  // Do revocation check

        // The default WinVerifyTrust Authenticode policy is to treat all time stamped
        // signatures as being valid forever. This OID limits the valid lifetime of the
        // signature to the lifetime of the certificate. This allows timestamped
        // signatures to expire. Normally this OID will be used in conjunction with
        // szOID_PKIX_KP_CODE_SIGNING to indicate new time stamp semantics should be
        // used. Support for this OID was added in WXP.


        internal const string szOID_KP_LIFETIME_SIGNING = "1.3.6.1.4.1.311.10.3.13";
        internal const string szOID_RSA_signingTime = "1.2.840.113549.1.9.5";

        //
        // Structures.
        //
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct CRYPT_DATA_BLOB
        {
            internal uint cbData;
            internal IntPtr pbData;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct AXL_SIGNER_INFO
        {
            internal uint cbSize;             // sizeof(AXL_SIGNER_INFO).
            internal uint dwError;            // Error code.
            internal uint algHash;            // Hash algorithm (ALG_ID).
            internal IntPtr pwszHash;           // Hash.
            internal IntPtr pwszDescription;    // Description.
            internal IntPtr pwszDescriptionUrl; // Description URL.
            internal IntPtr pChainContext;      // Signer's chain context.
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct AXL_TIMESTAMPER_INFO
        {
            internal uint cbSize;             // sizeof(AXL_TIMESTAMPER_INFO).
            internal uint dwError;            // Error code.
            internal uint algHash;            // Hash algorithm (ALG_ID).
            internal _FILETIME ftTimestamp;        // Timestamp time.
            internal IntPtr pChainContext;      // Timestamper's chain context.
        }

        //
        // DllImport declarations.
        //
        [DllImport(KERNEL32, CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern
        IntPtr GetProcessHeap();

        [DllImport(KERNEL32, CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern
        bool HeapFree(
            [In]    IntPtr hHeap,
            [In]    uint dwFlags,
            [In]    IntPtr lpMem);

        [DllImport(MSCORWKS, CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern
        int CertTimestampAuthenticodeLicense(
            [In]      ref CRYPT_DATA_BLOB pSignedLicenseBlob,
            [In]      string pwszTimestampURI,
            [In, Out]  ref CRYPT_DATA_BLOB pTimestampSignatureBlob);

        [DllImport(MSCORWKS, CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern
        int CertVerifyAuthenticodeLicense(
            [In]      ref CRYPT_DATA_BLOB pLicenseBlob,
            [In]      uint dwFlags,
            [In, Out]  ref AXL_SIGNER_INFO pSignerInfo,
            [In, Out]  ref AXL_TIMESTAMPER_INFO pTimestamperInfo);

        [DllImport(MSCORWKS, CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern
        int CertFreeAuthenticodeSignerInfo(
            [In]      ref AXL_SIGNER_INFO pSignerInfo);

        [DllImport(MSCORWKS, CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern
        int CertFreeAuthenticodeTimestamperInfo(
            [In]      ref AXL_TIMESTAMPER_INFO pTimestamperInfo);

        [DllImport(MSCORWKS, CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern
        int _AxlGetIssuerPublicKeyHash(
            [In]     IntPtr pCertContext,
            [In, Out] ref IntPtr ppwszPublicKeyHash);

        [DllImport(MSCORWKS, CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern
        int _AxlRSAKeyValueToPublicKeyToken(
            [In]     ref CRYPT_DATA_BLOB pModulusBlob,
            [In]     ref CRYPT_DATA_BLOB pExponentBlob,
            [In, Out] ref IntPtr ppwszPublicKeyToken);

        [DllImport(MSCORWKS, CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern
        int _AxlPublicKeyBlobToPublicKeyToken(
            [In]     ref CRYPT_DATA_BLOB pCspPublicKeyBlob,
            [In, Out] ref IntPtr ppwszPublicKeyToken);
    }

    class ManifestSignedXml : SignedXml
    {
        internal ManifestSignedXml() : base() { }
        internal ManifestSignedXml(XmlElement elem) : base(elem) { }
        internal ManifestSignedXml(XmlDocument document) : base(document) { }
    }

    class SignedCmiManifest
    {
        readonly XmlDocument m_manifestDom = null;

        SignedCmiManifest() { }

        internal SignedCmiManifest(XmlDocument manifestDom)
        {
            if (manifestDom == null)
            {
                throw new ArgumentNullException("manifestDom");
            }

            m_manifestDom = manifestDom;
        }

        internal void Sign(CmiManifestSigner signer)
        {
            Sign(signer, null);
        }

        internal void Sign(CmiManifestSigner signer, string timeStampUrl)
        {
            // Signer cannot be null.
            if (signer == null || signer.StrongNameKey == null)
            {
                throw new ArgumentNullException("signer");
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

        //
        // Privates.
        //
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

            var cspPublicKeyBlob = ((RSACryptoServiceProvider)snKey).ExportCspBlob(false);
            if (cspPublicKeyBlob == null || cspPublicKeyBlob.Length == 0)
            {
                throw new CryptographicException(Win32.NTE_BAD_KEY);
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
                using var sha1 = SHA1.Create();
                var hash = sha1.ComputeHash(exc.GetOutput() as MemoryStream);
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
                using var sha1 = SHA1.Create();
                var hash = sha1.ComputeHash(exc.GetOutput() as MemoryStream);
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

        static XmlDocument CreateLicenseDom(CmiManifestSigner signer, XmlElement principal, byte[] hash)
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

        static void AuthenticodeSignLicenseDom(XmlDocument licenseDom, CmiManifestSigner signer, string timeStampUrl)
        {
            // Make sure it is RSA, as this is the only one Fusion will support.
            //RSA rsaPublicKey = CngLightup.GetRSAPublicKey(signer.Certificate);
            var rsaPublicKey = signer.Certificate.GetRSAPublicKey();
            if (rsaPublicKey == null)
            {
                throw new NotSupportedException();
            }

            // Setup up XMLDSIG engine.
            var signedXml = new ManifestSignedXml(licenseDom);

            // HACK: Fix in a better way
            if (signer.Certificate.HasPrivateKey)
            {
                signedXml.SigningKey = signer.Certificate.GetRSAPrivateKey();
            }
            else if (signer.StrongNameKey is RSAKeyVault provider)
            {
                signedXml.SigningKey = provider;
            }
            else
            {
                throw new NotSupportedException();
            }

            signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;

            // Add the key information.
            signedXml.KeyInfo.AddClause(new RSAKeyValue(rsaPublicKey));
            signedXml.KeyInfo.AddClause(new KeyInfoX509Data(signer.Certificate, signer.IncludeOption));

            // Add the enveloped reference.
            var reference = new Reference();
            reference.Uri = "";

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

        static void StrongNameSignManifestDom(XmlDocument manifestDom, XmlDocument licenseDom, CmiManifestSigner signer)
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

            // Setup up XMLDSIG engine.
            var signedXml = new ManifestSignedXml(signatureParent);
            signedXml.SigningKey = signer.StrongNameKey;
            signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;

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

    [Flags]
    enum CmiManifestSignerFlag
    {
        None = 0x00000000,
        DontReplacePublicKeyToken = 0x00000001
    }

    [Flags]
    enum CmiManifestVerifyFlags
    {
        None = 0x00000000,
        RevocationNoCheck = 0x00000001,
        RevocationCheckEndCertOnly = 0x00000002,
        RevocationCheckEntireChain = 0x00000004,
        UrlCacheOnlyRetrieval = 0x00000008,
        LifetimeSigning = 0x00000010,
        TrustMicrosoftRootOnly = 0x00000020,
        StrongNameOnly = 0x00010000
    }

    class CmiManifestSigner
    {
        readonly AsymmetricAlgorithm m_strongNameKey;
        readonly X509Certificate2 m_certificate;
        string m_description;
        string m_url;
        readonly X509Certificate2Collection m_certificates;
        X509IncludeOption m_includeOption;
        CmiManifestSignerFlag m_signerFlag;

        CmiManifestSigner() { }

        internal CmiManifestSigner(AsymmetricAlgorithm strongNameKey) :
            this(strongNameKey, null)
        { }

        internal CmiManifestSigner(AsymmetricAlgorithm strongNameKey, X509Certificate2 certificate)
        {
            if (strongNameKey == null)
            {
                throw new ArgumentNullException("strongNameKey");
            }

#if (true) // 
            var rsa = strongNameKey as RSA;
            if (rsa == null)
            {
                throw new ArgumentNullException("strongNameKey");
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
                    throw new ArgumentException("value");
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
                        throw new ArgumentException("value");
                    }
                }
                m_signerFlag = value;
            }
        }

        internal const uint CimManifestSignerFlagMask = 0x00000001;
    }
}
#pragma warning restore IDE1006 // Naming Styles
#pragma warning restore IDE0003
#pragma warning restore IDE0032
#pragma warning restore IDE0049
#pragma warning restore IDE0016
#pragma warning restore IDE0017
#pragma warning restore IDE0018
#pragma warning restore IDE0019
#pragma warning restore IDE0029
#pragma warning restore IDE0051
