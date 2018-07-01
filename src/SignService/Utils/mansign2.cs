// ==++==
// 
//   Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// ==--==

//
// mansign.cs
//

// From https://github.com/Microsoft/referencesource/blob/7de0d30c7c5ef56ab60fee41fcdb50005d24979a/inc/mansign2.cs

using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Microsoft.Azure.KeyVault;
using Microsoft.Win32;

#pragma warning disable IDE0016 
#pragma warning disable IDE0017 
#pragma warning disable IDE0018 
#pragma warning disable IDE0019
#pragma warning disable IDE0029

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
        readonly bool m_verify = false;
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

        internal ManifestSignedXml2(XmlDocument document, bool verify)
            : base(document)
        {
            m_verify = verify;
            init();
        }

        void init()
        {
            CryptoConfig.AddAlgorithm(typeof(RSAPKCS1SHA256SignatureDescription),
                               Sha256SignatureMethodUri);

            CryptoConfig.AddAlgorithm(typeof(System.Security.Cryptography.SHA256Managed),
                               Sha256DigestMethod);
        }

        static XmlElement FindIdElement(XmlElement context, string idValue)
        {
            if (context == null)
            {
                return null;
            }

            var idReference = context.SelectSingleNode("//*[@Id=\"" + idValue + "\"]") as XmlElement;
            if (idReference != null)
            {
                return idReference;
            }

            idReference = context.SelectSingleNode("//*[@id=\"" + idValue + "\"]") as XmlElement;
            if (idReference != null)
            {
                return idReference;
            }

            return context.SelectSingleNode("//*[@ID=\"" + idValue + "\"]") as XmlElement;
        }

        public override XmlElement GetIdElement(XmlDocument document, string idValue)
        {
            // We only care about Id references inside of the KeyInfo section
            if (m_verify)
            {
                return base.GetIdElement(document, idValue);
            }

            var keyInfo = this.KeyInfo;
            if (keyInfo.Id != idValue)
            {
                return null;
            }

            return keyInfo.GetXml();
        }
    }

    class SignedCmiManifest2
    {
        XmlDocument m_manifestDom = null;
        CmiStrongNameSignerInfo m_strongNameSignerInfo = null;
        CmiAuthenticodeSignerInfo m_authenticodeSignerInfo = null;
        readonly bool m_useSha256;

        const string Sha256SignatureMethodUri = @"http://www.w3.org/2000/09/xmldsig#rsa-sha256";
        const string Sha256DigestMethod = @"http://www.w3.org/2000/09/xmldsig#sha256";

        const string wintrustPolicyFlagsRegPath = "Software\\Microsoft\\Windows\\CurrentVersion\\WinTrust\\Trust Providers\\Software Publishing";
        const string wintrustPolicyFlagsRegName = "State";

        SignedCmiManifest2() { }

        internal SignedCmiManifest2(XmlDocument manifestDom, bool useSha256)
        {
            if (manifestDom == null)
            {
                throw new ArgumentNullException("manifestDom");
            }

            m_manifestDom = manifestDom;
            m_useSha256 = useSha256;
        }

        internal void Sign(CmiManifestSigner2 signer)
        {
            Sign(signer, null);
        }

        internal void Sign(CmiManifestSigner2 signer, string timeStampUrl)
        {
            // Reset signer infos.
            m_strongNameSignerInfo = null;
            m_authenticodeSignerInfo = null;

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
                ReplacePublicKeyToken(m_manifestDom, signer.StrongNameKey, m_useSha256);
            }

            // No cert means don't Authenticode sign and timestamp.
            XmlDocument licenseDom = null;
            if (signer.Certificate != null)
            {
                // Yes. We will Authenticode sign, so first insert <publisherIdentity />
                // element, if necessary.
                InsertPublisherIdentity(m_manifestDom, signer.Certificate);

                // Now create the license DOM, and then sign it.
                licenseDom = CreateLicenseDom(signer, ExtractPrincipalFromManifest(), ComputeHashFromManifest(m_manifestDom, m_useSha256));
                AuthenticodeSignLicenseDom(licenseDom, signer, timeStampUrl, m_useSha256);
            }
            StrongNameSignManifestDom(m_manifestDom, licenseDom, signer, m_useSha256);
        }

        // throw cryptographic exception for any verification errors.
        internal void Verify(CmiManifestVerifyFlags verifyFlags)
        {
            // Reset signer infos.
            m_strongNameSignerInfo = null;
            m_authenticodeSignerInfo = null;

            var nsm = new XmlNamespaceManager(m_manifestDom.NameTable);
            nsm.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            var signatureNode = m_manifestDom.SelectSingleNode("//ds:Signature", nsm) as XmlElement;
            if (signatureNode == null)
            {
                throw new CryptographicException(Win32.TRUST_E_NOSIGNATURE);
            }

            // Make sure it is indeed SN signature, and it is an enveloped signature.
            var oldFormat = VerifySignatureForm(signatureNode, "StrongNameSignature", nsm);

            // It is the DSig we want, now make sure the public key matches the token.
            var publicKeyToken = VerifyPublicKeyToken();

            // OK. We found the SN signature with matching public key token, so
            // instantiate the SN signer info property.
            m_strongNameSignerInfo = new CmiStrongNameSignerInfo(Win32.TRUST_E_FAIL, publicKeyToken);

            // Now verify the SN signature, and Authenticode license if available.
            var signedXml = new ManifestSignedXml2(m_manifestDom, true);
            signedXml.LoadXml(signatureNode);
            if (m_useSha256)
            {
                signedXml.SignedInfo.SignatureMethod = Sha256SignatureMethodUri;
            }

            AsymmetricAlgorithm key = null;
            var dsigValid = signedXml.CheckSignatureReturningKey(out key);
            m_strongNameSignerInfo.PublicKey = key;
            if (!dsigValid)
            {
                m_strongNameSignerInfo.ErrorCode = Win32.TRUST_E_BAD_DIGEST;
                throw new CryptographicException(Win32.TRUST_E_BAD_DIGEST);
            }

            // Verify license as well if requested.
            if ((verifyFlags & CmiManifestVerifyFlags.StrongNameOnly) != CmiManifestVerifyFlags.StrongNameOnly)
            {
                if (m_useSha256)
                {
                    VerifyLicenseNew(verifyFlags, oldFormat);
                }
                else
                {
                    VerifyLicense(verifyFlags, oldFormat);
                }
            }
        }

        internal CmiStrongNameSignerInfo StrongNameSignerInfo
        {
            get
            {
                return m_strongNameSignerInfo;
            }
        }

        internal CmiAuthenticodeSignerInfo AuthenticodeSignerInfo
        {
            get
            {
                return m_authenticodeSignerInfo;
            }
        }

        //
        // Privates.
        //
        void VerifyLicense(CmiManifestVerifyFlags verifyFlags, bool oldFormat)
        {
            var nsm = new XmlNamespaceManager(m_manifestDom.NameTable);
            nsm.AddNamespace("asm", AssemblyNamespaceUri);
            nsm.AddNamespace("asm2", AssemblyV2NamespaceUri);
            nsm.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            nsm.AddNamespace("msrel", MSRelNamespaceUri);
            nsm.AddNamespace("r", LicenseNamespaceUri);
            nsm.AddNamespace("as", AuthenticodeNamespaceUri);

            // We are done if no license.
            var licenseNode = m_manifestDom.SelectSingleNode("asm:assembly/ds:Signature/ds:KeyInfo/msrel:RelData/r:license", nsm) as XmlElement;
            if (licenseNode == null)
            {
                return;
            }

            // Make sure this license is for this manifest.
            VerifyAssemblyIdentity(nsm);

            // Found a license, so instantiate signer info property.
            m_authenticodeSignerInfo = new CmiAuthenticodeSignerInfo(Win32.TRUST_E_FAIL);

            unsafe
            {
                var licenseXml = Encoding.UTF8.GetBytes(licenseNode.OuterXml);
                fixed (byte* pbLicense = licenseXml)
                {
                    var signerInfo = new Win32.AXL_SIGNER_INFO();
                    signerInfo.cbSize = (uint)Marshal.SizeOf(typeof(Win32.AXL_SIGNER_INFO));
                    var timestamperInfo = new Win32.AXL_TIMESTAMPER_INFO();
                    timestamperInfo.cbSize = (uint)Marshal.SizeOf(typeof(Win32.AXL_TIMESTAMPER_INFO));
                    var licenseBlob = new Win32.CRYPT_DATA_BLOB();
                    var pvLicense = new IntPtr(pbLicense);
                    licenseBlob.cbData = (uint)licenseXml.Length;
                    licenseBlob.pbData = pvLicense;

                    var hr = Win32.CertVerifyAuthenticodeLicense(ref licenseBlob, (uint)verifyFlags, ref signerInfo, ref timestamperInfo);
                    if (Win32.TRUST_E_NOSIGNATURE != (int)signerInfo.dwError)
                    {
                        m_authenticodeSignerInfo = new CmiAuthenticodeSignerInfo(signerInfo, timestamperInfo);
                    }

                    Win32.CertFreeAuthenticodeSignerInfo(ref signerInfo);
                    Win32.CertFreeAuthenticodeTimestamperInfo(ref timestamperInfo);

                    if (hr != Win32.S_OK)
                    {
                        throw new CryptographicException(hr);
                    }
                }
            }


#if (true) //
            if (!oldFormat)
            {
#endif
                // Make sure we have the intended Authenticode signer.
                VerifyPublisherIdentity(nsm);
            }
        }

        // can be used with sha1 or sha2 
        // logic is copied from the "isolation library" in NDP\iso_whid\ds\security\cryptoapi\pkisign\msaxlapi\mansign.cpp
        void VerifyLicenseNew(CmiManifestVerifyFlags verifyFlags, bool oldFormat)
        {
            var nsm = new XmlNamespaceManager(m_manifestDom.NameTable);
            nsm.AddNamespace("asm", AssemblyNamespaceUri);
            nsm.AddNamespace("asm2", AssemblyV2NamespaceUri);
            nsm.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            nsm.AddNamespace("msrel", MSRelNamespaceUri);
            nsm.AddNamespace("r", LicenseNamespaceUri);
            nsm.AddNamespace("as", AuthenticodeNamespaceUri);

            // We are done if no license.
            var licenseNode = m_manifestDom.SelectSingleNode("asm:assembly/ds:Signature/ds:KeyInfo/msrel:RelData/r:license", nsm) as XmlElement;
            if (licenseNode == null)
            {
                return;
            }

            // Make sure this license is for this manifest.
            VerifyAssemblyIdentity(nsm);

            // Found a license, so instantiate signer info property.
            m_authenticodeSignerInfo = new CmiAuthenticodeSignerInfo(Win32.TRUST_E_FAIL);

            // Find the license's signature
            var signatureNode = licenseNode.SelectSingleNode("//r:issuer/ds:Signature", nsm) as XmlElement;
            if (signatureNode == null)
            {
                throw new CryptographicException(Win32.TRUST_E_NOSIGNATURE);
            }

            // Make sure it is indeed an Authenticode signature, and it is an enveloped signature.
            // Then make sure the transforms are valid.
            VerifySignatureForm(signatureNode, "AuthenticodeSignature", nsm);

            // Now read the enveloped license signature.
            var licenseDom = new XmlDocument();
            licenseDom.LoadXml(licenseNode.OuterXml);
            signatureNode = licenseDom.SelectSingleNode("//r:issuer/ds:Signature", nsm) as XmlElement;

            var signedXml = new ManifestSignedXml2(licenseDom);
            signedXml.LoadXml(signatureNode);
            if (m_useSha256)
            {
                signedXml.SignedInfo.SignatureMethod = Sha256SignatureMethodUri;
            }

            // Check the signature
            if (!signedXml.CheckSignature())
            {
                m_authenticodeSignerInfo = null;
                throw new CryptographicException(Win32.TRUST_E_CERT_SIGNATURE);
            }

            var signingCertificate = GetSigningCertificate(signedXml, nsm);

            // First make sure certificate is not explicitly disallowed.
            var store = new X509Store(StoreName.Disallowed, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            X509Certificate2Collection storedCertificates = null;
            try
            {
                storedCertificates = store.Certificates;
                if (storedCertificates == null)
                {
                    m_authenticodeSignerInfo.ErrorCode = Win32.TRUST_E_FAIL;
                    throw new CryptographicException(Win32.TRUST_E_FAIL);
                }
                if (storedCertificates.Contains(signingCertificate))
                {
                    m_authenticodeSignerInfo.ErrorCode = Win32.TRUST_E_EXPLICIT_DISTRUST;
                    throw new CryptographicException(Win32.TRUST_E_EXPLICIT_DISTRUST);
                }
            }
            finally
            {
                store.Close();
            }

            // prepare information for the TrustManager to display
            string hash;
            string description;
            string url;
            if (!GetManifestInformation(licenseNode, nsm, out hash, out description, out url))
            {
                m_authenticodeSignerInfo.ErrorCode = Win32.TRUST_E_SUBJECT_FORM_UNKNOWN;
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }
            m_authenticodeSignerInfo.Hash = hash;
            m_authenticodeSignerInfo.Description = description;
            m_authenticodeSignerInfo.DescriptionUrl = url;

            // read the timestamp from the manifest
            DateTime verificationTime;
            var isTimestamped = VerifySignatureTimestamp(signatureNode, nsm, out verificationTime);
            var isLifetimeSigning = false;
            if (isTimestamped)
            {
                isLifetimeSigning = ((verifyFlags & CmiManifestVerifyFlags.LifetimeSigning) == CmiManifestVerifyFlags.LifetimeSigning);
                if (!isLifetimeSigning)
                {
                    isLifetimeSigning = GetLifetimeSigning(signingCertificate);
                }
            }

            // Retrieve the Authenticode policy settings from registry.
            var policies = GetAuthenticodePolicies();

            var chain = new X509Chain(); // use the current user profile
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            if ((CmiManifestVerifyFlags.RevocationCheckEndCertOnly & verifyFlags) == CmiManifestVerifyFlags.RevocationCheckEndCertOnly)
            {
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EndCertificateOnly;
            }
            else if ((CmiManifestVerifyFlags.RevocationCheckEntireChain & verifyFlags) == CmiManifestVerifyFlags.RevocationCheckEntireChain)
            {
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            }
            else if (((CmiManifestVerifyFlags.RevocationNoCheck & verifyFlags) == CmiManifestVerifyFlags.RevocationNoCheck) ||
                ((Win32.WTPF_IGNOREREVOKATION & policies) == Win32.WTPF_IGNOREREVOKATION))
            {
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            }

            chain.ChainPolicy.VerificationTime = verificationTime; // local time
            if (isTimestamped && isLifetimeSigning)
            {
                chain.ChainPolicy.ApplicationPolicy.Add(new Oid(Win32.szOID_KP_LIFETIME_SIGNING));
            }

            chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 1, 0);
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag; // don't ignore anything

            var chainIsValid = chain.Build(signingCertificate);

            if (!chainIsValid)
            {
#if DEBUG
                var statuses = chain.ChainStatus;
                foreach (var status in statuses)
                {
                    System.Diagnostics.Debug.WriteLine("flag = " + status.Status + " " + status.StatusInformation);
                }
#endif
                AuthenticodeSignerInfo.ErrorCode = Win32.TRUST_E_SUBJECT_NOT_TRUSTED;
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_NOT_TRUSTED);
            }

            // package information for the trust manager
            m_authenticodeSignerInfo.SignerChain = chain;

            store = new X509Store(StoreName.TrustedPublisher, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            try
            {
                storedCertificates = store.Certificates;
                if (storedCertificates == null)
                {
                    m_authenticodeSignerInfo.ErrorCode = Win32.TRUST_E_FAIL;
                    throw new CryptographicException(Win32.TRUST_E_FAIL);
                }
                if (!storedCertificates.Contains(signingCertificate))
                {
                    AuthenticodeSignerInfo.ErrorCode = Win32.TRUST_E_SUBJECT_NOT_TRUSTED;
                    throw new CryptographicException(Win32.TRUST_E_SUBJECT_NOT_TRUSTED);
                }
            }
            finally
            {
                store.Close();
            }

            // Verify Certificate publisher name
            var subjectNode = licenseNode.SelectSingleNode("r:grant/as:AuthenticodePublisher/as:X509SubjectName", nsm) as XmlElement;
            if (subjectNode == null || String.Compare(signingCertificate.Subject, subjectNode.InnerText, StringComparison.Ordinal) != 0)
            {
                AuthenticodeSignerInfo.ErrorCode = Win32.TRUST_E_CERT_SIGNATURE;
                throw new CryptographicException(Win32.TRUST_E_CERT_SIGNATURE);
            }

#if (true) //
            if (!oldFormat)
            {
#endif
                // Make sure we have the intended Authenticode signer.
                VerifyPublisherIdentity(nsm);
            }
        }

        X509Certificate2 GetSigningCertificate(ManifestSignedXml2 signedXml, XmlNamespaceManager nsm)
        {
            X509Certificate2 signingCertificate = null;

            var keyInfo = signedXml.KeyInfo;
            KeyInfoX509Data kiX509 = null;
            RSAKeyValue keyValue = null;
            foreach (KeyInfoClause kic in keyInfo)
            {
                if (keyValue == null)
                {
                    keyValue = kic as RSAKeyValue;
                    if (keyValue == null)
                    {
                        break;
                    }
                }

                if (kiX509 == null)
                {
                    kiX509 = kic as KeyInfoX509Data;
                }

                if (keyValue != null && kiX509 != null)
                {
                    break;
                }
            }

            if (keyValue == null || kiX509 == null)
            {
                // no X509Certificate KeyInfoClause
                m_authenticodeSignerInfo.ErrorCode = Win32.TRUST_E_SUBJECT_FORM_UNKNOWN;
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            // get public key from signing keyInfo
            RSAParameters signingPublicKey;
            var rsaProvider = keyValue.Key;
            if (rsaProvider != null)
            {
                signingPublicKey = rsaProvider.ExportParameters(false);
            }
            else
            {
                m_authenticodeSignerInfo.ErrorCode = Win32.TRUST_E_CERT_SIGNATURE;
                throw new CryptographicException(Win32.TRUST_E_CERT_SIGNATURE);
            }

            // enumerate all certificates in x509Data searching for the one whose public key is used in <RSAKeyValue>
            foreach (X509Certificate2 certificate in kiX509.Certificates)
            {
                if (certificate == null)
                {
                    continue;
                }

                var certificateAuthority = false;
                foreach (var extention in certificate.Extensions)
                {
                    var basicExtention = extention as X509BasicConstraintsExtension;
                    if (basicExtention != null)
                    {
                        certificateAuthority = basicExtention.CertificateAuthority;
                        if (certificateAuthority)
                        {
                            break;
                        }
                    }
                }

                if (certificateAuthority)
                {
                    // Ignore certs that have "Subject Type=CA" in basic contraints
                    continue;
                }

                //RSA publicKey = CngLightup.GetRSAPublicKey(certificate);
                var publicKey = certificate.GetRSAPublicKey();
                var certificatePublicKey = publicKey.ExportParameters(false);
                if ((StructuralComparisons.StructuralEqualityComparer.Equals(signingPublicKey.Exponent, certificatePublicKey.Exponent))
                   && (StructuralComparisons.StructuralEqualityComparer.Equals(signingPublicKey.Modulus, certificatePublicKey.Modulus)))
                {
                    signingCertificate = certificate;
                    break;
                }
            }

            if (signingCertificate == null)
            {
                m_authenticodeSignerInfo.ErrorCode = Win32.TRUST_E_CERT_SIGNATURE;
                throw new CryptographicException(Win32.TRUST_E_CERT_SIGNATURE);
            }
            return signingCertificate;
        }

        bool VerifySignatureForm(XmlElement signatureNode, string signatureKind, XmlNamespaceManager nsm)
        {
            var oldFormat = false;
            var snIdName = "Id";
            if (!signatureNode.HasAttribute(snIdName))
            {
                snIdName = "id";
                if (!signatureNode.HasAttribute(snIdName))
                {
                    snIdName = "ID";
                    if (!signatureNode.HasAttribute(snIdName))
                    {
                        throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
                    }
                }
            }

            var snIdValue = signatureNode.GetAttribute(snIdName);
            if (snIdValue == null ||
                String.Compare(snIdValue, signatureKind, StringComparison.Ordinal) != 0)
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            // Make sure it is indeed an enveloped signature.
            var validFormat = false;
            var referenceNodes = signatureNode.SelectNodes("ds:SignedInfo/ds:Reference", nsm);
            foreach (XmlNode referenceNode in referenceNodes)
            {
                var reference = referenceNode as XmlElement;
                if (reference != null && reference.HasAttribute("URI"))
                {
                    var uriValue = reference.GetAttribute("URI");
                    if (uriValue != null)
                    {
                        // We expect URI="" (empty URI value which means to hash the entire document).
                        if (uriValue.Length == 0)
                        {
                            var transformsNode = reference.SelectSingleNode("ds:Transforms", nsm);
                            if (transformsNode == null)
                            {
                                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
                            }

                            // Make sure the transforms are what we expected.
                            var transforms = transformsNode.SelectNodes("ds:Transform", nsm);
                            if (transforms.Count < 2)
                            {
                                // We expect at least:
                                //  <Transform Algorithm="http://www.w3.org/2001/10/xml-exc-c14n#" />
                                //  <Transform Algorithm="http://www.w3.org/2000/09/xmldsig#enveloped-signature" /> 
                                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
                            }

                            var c14 = false;
                            var enveloped = false;
                            for (var i = 0; i < transforms.Count; i++)
                            {
                                var transform = transforms[i] as XmlElement;
                                var algorithm = transform.GetAttribute("Algorithm");
                                if (algorithm == null)
                                {
                                    break;
                                }
                                else if (String.Compare(algorithm, SignedXml.XmlDsigExcC14NTransformUrl, StringComparison.Ordinal) != 0)
                                {
                                    c14 = true;
                                    if (enveloped)
                                    {
                                        validFormat = true;
                                        break;
                                    }
                                }
                                else if (String.Compare(algorithm, SignedXml.XmlDsigEnvelopedSignatureTransformUrl, StringComparison.Ordinal) != 0)
                                {
                                    enveloped = true;
                                    if (c14)
                                    {
                                        validFormat = true;
                                        break;
                                    }
                                }
                            }
                        }
#if (true) // 
                        else if (String.Compare(uriValue, "#StrongNameKeyInfo", StringComparison.Ordinal) == 0)
                        {
                            oldFormat = true;

                            var transformsNode = referenceNode.SelectSingleNode("ds:Transforms", nsm);
                            if (transformsNode == null)
                            {
                                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
                            }

                            // Make sure the transforms are what we expected.
                            var transforms = transformsNode.SelectNodes("ds:Transform", nsm);
                            if (transforms.Count < 1)
                            {
                                // We expect at least:
                                //  <Transform Algorithm="http://www.w3.org/2001/10/xml-exc-c14n#" />
                                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
                            }

                            for (var i = 0; i < transforms.Count; i++)
                            {
                                var transform = transforms[i] as XmlElement;
                                var algorithm = transform.GetAttribute("Algorithm");
                                if (algorithm == null)
                                {
                                    break;
                                }
                                else if (String.Compare(algorithm, SignedXml.XmlDsigExcC14NTransformUrl, StringComparison.Ordinal) != 0)
                                {
                                    validFormat = true;
                                    break;
                                }
                            }
                        }
#endif // 
                    }
                }
            }

            if (!validFormat)
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            return oldFormat;
        }

        bool GetManifestInformation(XmlElement licenseNode, XmlNamespaceManager nsm, out string hash, out string description, out string url)
        {
            hash = "";
            description = "";
            url = "";

            var manifestInformation = licenseNode.SelectSingleNode("r:grant/as:ManifestInformation", nsm) as XmlElement;
            if (manifestInformation == null)
            {
                return false;
            }
            if (!manifestInformation.HasAttribute("Hash"))
            {
                return false;
            }

            hash = manifestInformation.GetAttribute("Hash");
            if (string.IsNullOrEmpty(hash))
            {
                return false;
            }

            foreach (var c in hash)
            {
                if (0xFF == HexToByte(c))
                {
                    return false;
                }
            }

            if (manifestInformation.HasAttribute("Description"))
            {
                description = manifestInformation.GetAttribute("Description");
            }

            if (manifestInformation.HasAttribute("Url"))
            {
                url = manifestInformation.GetAttribute("Url");
            }

            return true;
        }

        bool VerifySignatureTimestamp(XmlElement signatureNode, XmlNamespaceManager nsm, out DateTime verificationTime)
        {
            throw new NotImplementedException("These types are not supported with .NET Core 2 yet");
            //verificationTime = DateTime.Now;

            //XmlElement node = signatureNode.SelectSingleNode("ds:Object/as:Timestamp", nsm) as XmlElement;
            //if (node != null)
            //{
            //    string encodedMessage = node.InnerText;

            //    if (!string.IsNullOrEmpty(encodedMessage))
            //    {
            //        byte[] base64DecodedMessage = null;
            //        try
            //        {
            //            base64DecodedMessage = Convert.FromBase64String(encodedMessage);
            //        }
            //        catch (FormatException)
            //        {
            //            m_authenticodeSignerInfo.ErrorCode = Win32.TRUST_E_TIME_STAMP;
            //            throw new CryptographicException(Win32.TRUST_E_TIME_STAMP);
            //        }
            //        if (base64DecodedMessage != null)
            //        {
            //            // Create a new, nondetached SignedCms message.
            //            SignedCms signedCms = new SignedCms();
            //            signedCms.Decode(base64DecodedMessage);

            //            // Verify the signature without validating the 
            //            // certificate.
            //            signedCms.CheckSignature(true);

            //            byte[] signingTime = null;
            //            CryptographicAttributeObjectCollection caos = signedCms.SignerInfos[0].SignedAttributes;
            //            foreach (CryptographicAttributeObject cao in caos)
            //            {
            //                if (0 == string.Compare(cao.Oid.Value, Win32.szOID_RSA_signingTime, StringComparison.Ordinal))
            //                {
            //                    foreach (AsnEncodedData d in cao.Values)
            //                    {
            //                        if (0 == string.Compare(d.Oid.Value, Win32.szOID_RSA_signingTime, StringComparison.Ordinal))
            //                        {
            //                            signingTime = d.RawData;
            //                            Pkcs9SigningTime time = new Pkcs9SigningTime(signingTime);
            //                            verificationTime = time.SigningTime;
            //                            return true;
            //                        }
            //                    }
            //                }
            //            }
            //        }
            //    }
            //}

            //return false;
        }

        bool GetLifetimeSigning(X509Certificate2 signingCertificate)
        {
            foreach (var extension in signingCertificate.Extensions)
            {
                var ekuExtention = extension as X509EnhancedKeyUsageExtension;
                if (ekuExtention != null)
                {
                    var oids = ekuExtention.EnhancedKeyUsages;
                    foreach (var oid in oids)
                    {
                        if (0 == string.Compare(Win32.szOID_KP_LIFETIME_SIGNING, oid.Value, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // Retrieve the Authenticode policy settings from registry. 
        // Isolation library was ignoring missing or inaccessible key/value errors
        uint GetAuthenticodePolicies()
        {
            uint policies = 0;

            try
            {
                var key = Registry.CurrentUser.OpenSubKey(wintrustPolicyFlagsRegPath);
                if (key != null)
                {
                    var kind = key.GetValueKind(wintrustPolicyFlagsRegName);
                    if (kind == RegistryValueKind.DWord || kind == RegistryValueKind.Binary)
                    {
                        var value = key.GetValue(wintrustPolicyFlagsRegName);
                        if (value != null)
                        {
                            policies = Convert.ToUInt32(value);
                        }
                    }
                    key.Close();
                }
            }
            catch (System.Security.SecurityException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
            return policies;
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

        void VerifyAssemblyIdentity(XmlNamespaceManager nsm)
        {
            var assemblyIdentity = m_manifestDom.SelectSingleNode("asm:assembly/asm:assemblyIdentity", nsm) as XmlElement;
            var principal = m_manifestDom.SelectSingleNode("asm:assembly/ds:Signature/ds:KeyInfo/msrel:RelData/r:license/r:grant/as:ManifestInformation/as:assemblyIdentity", nsm) as XmlElement;

            if (assemblyIdentity == null || principal == null ||
                !assemblyIdentity.HasAttributes || !principal.HasAttributes)
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            var asmIdAttrs = assemblyIdentity.Attributes;

            if (asmIdAttrs.Count == 0 || asmIdAttrs.Count != principal.Attributes.Count)
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            foreach (XmlAttribute asmIdAttr in asmIdAttrs)
            {
                if (!principal.HasAttribute(asmIdAttr.LocalName) ||
                    asmIdAttr.Value != principal.GetAttribute(asmIdAttr.LocalName))
                {
                    throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
                }
            }

            VerifyHash(nsm);
        }

        void VerifyPublisherIdentity(XmlNamespaceManager nsm)
        {
            // Nothing to do if no signature.
            if (m_authenticodeSignerInfo.ErrorCode == Win32.TRUST_E_NOSIGNATURE)
            {
                return;
            }

            var signerCert = m_authenticodeSignerInfo.SignerChain.ChainElements[0].Certificate;

            // Find the publisherIdentity element.
            var publisherIdentity = m_manifestDom.SelectSingleNode("asm:assembly/asm2:publisherIdentity", nsm) as XmlElement;
            if (publisherIdentity == null || !publisherIdentity.HasAttributes)
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            // Get name and issuerKeyHash attribute values.
            if (!publisherIdentity.HasAttribute("name") || !publisherIdentity.HasAttribute("issuerKeyHash"))
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            var publisherName = publisherIdentity.GetAttribute("name");
            var publisherIssuerKeyHash = publisherIdentity.GetAttribute("issuerKeyHash");

            // Calculate the issuer key hash.
            var pIssuerKeyHash = new IntPtr();
            var hr = Win32._AxlGetIssuerPublicKeyHash(signerCert.Handle, ref pIssuerKeyHash);
            if (hr != Win32.S_OK)
            {
                throw new CryptographicException(hr);
            }

            var issuerKeyHash = Marshal.PtrToStringUni(pIssuerKeyHash);
            Win32.HeapFree(Win32.GetProcessHeap(), 0, pIssuerKeyHash);

            // Make sure name and issuerKeyHash match.
            if (String.Compare(publisherName, signerCert.SubjectName.Name, StringComparison.Ordinal) != 0 ||
                String.Compare(publisherIssuerKeyHash, issuerKeyHash, StringComparison.Ordinal) != 0)
            {
                throw new CryptographicException(Win32.TRUST_E_FAIL);
            }
        }

        void VerifyHash(XmlNamespaceManager nsm)
        {
            var manifestDom = new XmlDocument();
            // We always preserve white space as Fusion XML engine always preserve white space.
            manifestDom.PreserveWhitespace = true;
            manifestDom = (XmlDocument)m_manifestDom.Clone();

            var manifestInformation = manifestDom.SelectSingleNode("asm:assembly/ds:Signature/ds:KeyInfo/msrel:RelData/r:license/r:grant/as:ManifestInformation", nsm) as XmlElement;
            if (manifestInformation == null)
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            if (!manifestInformation.HasAttribute("Hash"))
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            var hash = manifestInformation.GetAttribute("Hash");
            if (hash == null || hash.Length == 0)
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            // Now compute the hash for the manifest without the entire SN
            // signature element.

            // First remove the Signture element from the DOM.
            var dsElement = manifestDom.SelectSingleNode("asm:assembly/ds:Signature", nsm) as XmlElement;
            if (dsElement == null)
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            dsElement.ParentNode.RemoveChild(dsElement);

            // Now compute the hash from the manifest, without the Signature element.
            var hashBytes = HexStringToBytes(manifestInformation.GetAttribute("Hash"));
            var computedHashBytes = ComputeHashFromManifest(manifestDom, m_useSha256);

            // Do they match?
            if (hashBytes.Length == 0 || hashBytes.Length != computedHashBytes.Length)
            {
#if (true) // 
                var computedOldHashBytes = ComputeHashFromManifest(manifestDom, true, m_useSha256);

                // Do they match?
                if (hashBytes.Length == 0 || hashBytes.Length != computedOldHashBytes.Length)
                {
                    throw new CryptographicException(Win32.TRUST_E_BAD_DIGEST);
                }

                for (var i = 0; i < hashBytes.Length; i++)
                {
                    if (hashBytes[i] != computedOldHashBytes[i])
                    {
                        throw new CryptographicException(Win32.TRUST_E_BAD_DIGEST);
                    }
                }
#else
                throw new CryptographicException(Win32.TRUST_E_BAD_DIGEST);
#endif
            }

            for (var i = 0; i < hashBytes.Length; i++)
            {
                if (hashBytes[i] != computedHashBytes[i])
                {
#if (true) // 
                    var computedOldHashBytes = ComputeHashFromManifest(manifestDom, true, m_useSha256);

                    // Do they match?
                    if (hashBytes.Length == 0 || hashBytes.Length != computedOldHashBytes.Length)
                    {
                        throw new CryptographicException(Win32.TRUST_E_BAD_DIGEST);
                    }

                    for (i = 0; i < hashBytes.Length; i++)
                    {
                        if (hashBytes[i] != computedOldHashBytes[i])
                        {
                            throw new CryptographicException(Win32.TRUST_E_BAD_DIGEST);
                        }
                    }
#else
                throw new CryptographicException(Win32.TRUST_E_BAD_DIGEST);
#endif
                }
            }
        }

        string VerifyPublicKeyToken()
        {
            var nsm = new XmlNamespaceManager(m_manifestDom.NameTable);
            nsm.AddNamespace("asm", AssemblyNamespaceUri);
            nsm.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

            var snModulus = m_manifestDom.SelectSingleNode("asm:assembly/ds:Signature/ds:KeyInfo/ds:KeyValue/ds:RSAKeyValue/ds:Modulus", nsm) as XmlElement;
            var snExponent = m_manifestDom.SelectSingleNode("asm:assembly/ds:Signature/ds:KeyInfo/ds:KeyValue/ds:RSAKeyValue/ds:Exponent", nsm) as XmlElement;

            if (snModulus == null || snExponent == null)
            {
                throw new CryptographicException(Win32.TRUST_E_SUBJECT_FORM_UNKNOWN);
            }

            var modulus = Encoding.UTF8.GetBytes(snModulus.InnerXml);
            var exponent = Encoding.UTF8.GetBytes(snExponent.InnerXml);

            var tokenString = GetPublicKeyToken(m_manifestDom);
            var publicKeyToken = HexStringToBytes(tokenString);
            byte[] computedPublicKeyToken;

            unsafe
            {
                fixed (byte* pbModulus = modulus)
                {
                    fixed (byte* pbExponent = exponent)
                    {
                        var modulusBlob = new Win32.CRYPT_DATA_BLOB();
                        var exponentBlob = new Win32.CRYPT_DATA_BLOB();
                        var pComputedToken = new IntPtr();

                        modulusBlob.cbData = (uint)modulus.Length;
                        modulusBlob.pbData = new IntPtr(pbModulus);
                        exponentBlob.cbData = (uint)exponent.Length;
                        exponentBlob.pbData = new IntPtr(pbExponent);

                        // Now compute the public key token.
                        var hr = Win32._AxlRSAKeyValueToPublicKeyToken(ref modulusBlob, ref exponentBlob, ref pComputedToken);
                        if (hr != Win32.S_OK)
                        {
                            throw new CryptographicException(hr);
                        }

                        computedPublicKeyToken = HexStringToBytes(Marshal.PtrToStringUni(pComputedToken));
                        Win32.HeapFree(Win32.GetProcessHeap(), 0, pComputedToken);
                    }
                }
            }

            // Do they match?
            if (publicKeyToken.Length == 0 || publicKeyToken.Length != computedPublicKeyToken.Length)
            {
                throw new CryptographicException(Win32.TRUST_E_FAIL);
            }

            for (var i = 0; i < publicKeyToken.Length; i++)
            {
                if (publicKeyToken[i] != computedPublicKeyToken[i])
                {
                    throw new CryptographicException(Win32.TRUST_E_FAIL);
                }
            }

            return tokenString;
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
        internal static RSACryptoServiceProvider GetFixedRSACryptoServiceProvider(RSACryptoServiceProvider oldCsp, bool useSha256)
        {
            if (!useSha256)
            {
                return oldCsp;
            }

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

        static void ReplacePublicKeyToken(XmlDocument manifestDom, AsymmetricAlgorithm snKey, bool useSha256)
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

            if (snKey is RSACryptoServiceProvider)
            {
                cspPublicKeyBlob = (GetFixedRSACryptoServiceProvider((RSACryptoServiceProvider)snKey, useSha256)).ExportCspBlob(false);
                if (cspPublicKeyBlob == null || cspPublicKeyBlob.Length == 0)
                {
                    throw new CryptographicException(Win32.NTE_BAD_KEY);
                }
            }
            else
            {
                using (var rsaCsp = new RSACryptoServiceProvider())
                {
                    rsaCsp.ImportParameters(((RSA)snKey).ExportParameters(false));
                    cspPublicKeyBlob = rsaCsp.ExportCspBlob(false);
                }
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

        static byte[] ComputeHashFromManifest(XmlDocument manifestDom, bool useSha256)
        {
#if (true) // 
            return ComputeHashFromManifest(manifestDom, false, useSha256);
        }

        static byte[] ComputeHashFromManifest(XmlDocument manifestDom, bool oldFormat, bool useSha256)
        {
            if (oldFormat)
            {
                var exc = new XmlDsigExcC14NTransform();
                exc.LoadInput(manifestDom);

                if (useSha256)
                {
                    using (var sha2 = new SHA256CryptoServiceProvider())
                    {
                        var hash = sha2.ComputeHash(exc.GetOutput() as MemoryStream);
                        if (hash == null)
                        {
                            throw new CryptographicException(Win32.TRUST_E_BAD_DIGEST);
                        }

                        return hash;
                    }
                }
                else
                {
                    using (var sha1 = new SHA1CryptoServiceProvider())
                    {
                        var hash = sha1.ComputeHash(exc.GetOutput() as MemoryStream);
                        if (hash == null)
                        {
                            throw new CryptographicException(Win32.TRUST_E_BAD_DIGEST);
                        }

                        return hash;
                    }
                }
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

                if (useSha256)
                {
                    using (var sha2 = new SHA256CryptoServiceProvider())
                    {
                        var hash = sha2.ComputeHash(exc.GetOutput() as MemoryStream);
                        if (hash == null)
                        {
                            throw new CryptographicException(Win32.TRUST_E_BAD_DIGEST);
                        }

                        return hash;
                    }
                }
                else
                {
                    using (var sha1 = new SHA1CryptoServiceProvider())
                    {
                        var hash = sha1.ComputeHash(exc.GetOutput() as MemoryStream);
                        if (hash == null)
                        {
                            throw new CryptographicException(Win32.TRUST_E_BAD_DIGEST);
                        }

                        return hash;
                    }
                }

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

        static void AuthenticodeSignLicenseDom(XmlDocument licenseDom, CmiManifestSigner2 signer, string timeStampUrl, bool useSha256)
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
                if (signer.UseSha256)
                {
                    signedXml.SignedInfo.SignatureMethod = Sha256SignatureMethodUri;
                }

                // Add the key information.
                signedXml.KeyInfo.AddClause(new RSAKeyValue(rsaPrivateKey));
                signedXml.KeyInfo.AddClause(new KeyInfoX509Data(signer.Certificate, signer.IncludeOption));

                // Add the enveloped reference.
                var reference = new Reference();
                reference.Uri = "";
                if (signer.UseSha256)
                {
                    reference.DigestMethod = Sha256DigestMethod;
                }

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

        static void StrongNameSignManifestDom(XmlDocument manifestDom, XmlDocument licenseDom, CmiManifestSigner2 signer, bool useSha256)
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

            if (!(signer.StrongNameKey is RSA))
            {
                throw new NotSupportedException();
            }

            // Setup up XMLDSIG engine.
            var signedXml = new ManifestSignedXml2(signatureParent);
            if (signer.StrongNameKey is RSACryptoServiceProvider)
            {
                signedXml.SigningKey = GetFixedRSACryptoServiceProvider(signer.StrongNameKey as RSACryptoServiceProvider, useSha256);
            }
            else
            {
                signedXml.SigningKey = signer.StrongNameKey;
            }
            signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
            if (signer.UseSha256)
            {
                signedXml.SignedInfo.SignatureMethod = Sha256SignatureMethodUri;
            }

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
            if (signer.UseSha256)
            {
                enveloped.DigestMethod = Sha256DigestMethod;
            }

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

        static byte[] HexStringToBytes(string hexString)
        {
            var cbHex = (uint)hexString.Length / 2;
            var hex = new byte[cbHex];
            var i = hexString.Length - 2;
            for (var index = 0; index < cbHex; index++)
            {
                hex[index] = (byte)((HexToByte(hexString[i]) << 4) | HexToByte(hexString[i + 1]));
                i -= 2;
            }
            return hex;
        }

        static byte HexToByte(char val)
        {
            if (val <= '9' && val >= '0')
            {
                return (byte)(val - '0');
            }
            else if (val >= 'a' && val <= 'f')
            {
                return (byte)((val - 'a') + 10);
            }
            else if (val >= 'A' && val <= 'F')
            {
                return (byte)((val - 'A') + 10);
            }
            else
            {
                return 0xFF;
            }
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
        readonly bool m_useSha256;

        CmiManifestSigner2() { }

        internal CmiManifestSigner2(AsymmetricAlgorithm strongNameKey) :
            this(strongNameKey, null, false)
        { }

        internal CmiManifestSigner2(AsymmetricAlgorithm strongNameKey, X509Certificate2 certificate, bool useSha256)
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
            m_useSha256 = useSha256;
        }

        internal bool UseSha256
        {
            get
            {
                return m_useSha256;
            }
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
#pragma warning restore IDE0016
#pragma warning restore IDE0017
#pragma warning restore IDE0018
#pragma warning restore IDE0019
#pragma warning restore IDE0029