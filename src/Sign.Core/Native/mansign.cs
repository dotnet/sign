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

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

using _FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

// From: https://github.com/Microsoft/referencesource/blob/7de0d30c7c5ef56ab60fee41fcdb50005d24979a/inc/mansign.cs
#pragma warning disable IDE0049

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
            [In] IntPtr hHeap,
            [In] uint dwFlags,
            [In] IntPtr lpMem);

        [DllImport(MSCORWKS, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern
        int CertTimestampAuthenticodeLicense(
            [In] ref CRYPT_DATA_BLOB pSignedLicenseBlob,
            [In] string pwszTimestampURI,
            [In, Out] ref CRYPT_DATA_BLOB pTimestampSignatureBlob);

        [DllImport(MSCORWKS, CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern
        int CertVerifyAuthenticodeLicense(
            [In] ref CRYPT_DATA_BLOB pLicenseBlob,
            [In] uint dwFlags,
            [In, Out] ref AXL_SIGNER_INFO pSignerInfo,
            [In, Out] ref AXL_TIMESTAMPER_INFO pTimestamperInfo);

        [DllImport(MSCORWKS, CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern
        int CertFreeAuthenticodeSignerInfo(
            [In] ref AXL_SIGNER_INFO pSignerInfo);

        [DllImport(MSCORWKS, CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern
        int CertFreeAuthenticodeTimestamperInfo(
            [In] ref AXL_TIMESTAMPER_INFO pTimestamperInfo);

        [DllImport(MSCORWKS, CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern
        int _AxlGetIssuerPublicKeyHash(
            [In] IntPtr pCertContext,
            [In, Out] ref IntPtr ppwszPublicKeyHash);

        [DllImport(MSCORWKS, CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern
        int _AxlRSAKeyValueToPublicKeyToken(
            [In] ref CRYPT_DATA_BLOB pModulusBlob,
            [In] ref CRYPT_DATA_BLOB pExponentBlob,
            [In, Out] ref IntPtr ppwszPublicKeyToken);

        [DllImport(MSCORWKS, CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern
        int _AxlPublicKeyBlobToPublicKeyToken(
            [In] ref CRYPT_DATA_BLOB pCspPublicKeyBlob,
            [In, Out] ref IntPtr ppwszPublicKeyToken);
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
}