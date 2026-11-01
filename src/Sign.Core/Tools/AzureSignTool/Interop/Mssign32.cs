// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Runtime.InteropServices;

namespace Sign.Core.Interop
{
    internal static class Mssign32
    {
        [DllImport("mssign32.dll", CallingConvention = CallingConvention.Winapi)]
        public static unsafe extern int SignerSignEx3(
            SignerSignEx3Flags dwFlags,
            SIGNER_SUBJECT_INFO* pSubjectInfo,
            SIGNER_CERT* pSignerCert,
            SIGNER_SIGNATURE_INFO* pSignatureInfo,
            IntPtr pProviderInfo,
            SignerSignTimeStampFlags dwTimestampFlags,
            byte* pszTimestampAlgorithmOid,
            char* pwszHttpTimeStamp,
            IntPtr psRequest,
            void* pSipData,
            IntPtr* ppSignerContext,
            IntPtr pCryptoPolicy,
            SIGN_INFO* pSignInfo,
            IntPtr pReserved);

        [DllImport("mssign32.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int SignerFreeSignerContext(IntPtr pSignerContext);
    }

    // All fields are required to preserve the native structure layouts.
    [StructLayout(LayoutKind.Sequential)]
    internal struct SIGNER_SIGNATURE_INFO
    {
        public uint cbSize;
        public uint algidHash;
        public SignerSignatureInfoAttrChoice dwAttrChoice;
        public SIGNER_SIGNATURE_INFO_UNION attrAuthUnion;
        public IntPtr psAuthenticated;
        public IntPtr psUnauthenticated;

        public SIGNER_SIGNATURE_INFO(uint algidHash, SIGNER_SIGNATURE_INFO_UNION attrAuthUnion)
        {
            cbSize = (uint)Marshal.SizeOf<SIGNER_SIGNATURE_INFO>();
            this.algidHash = algidHash;
            dwAttrChoice = SignerSignatureInfoAttrChoice.SIGNER_AUTHCODE_ATTR;
            this.attrAuthUnion = attrAuthUnion;
            psAuthenticated = IntPtr.Zero;
            psUnauthenticated = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct SIGNER_SIGNATURE_INFO_UNION
    {
        public SIGNER_SIGNATURE_INFO_UNION(SIGNER_ATTR_AUTHCODE* pAttrAuthcode)
        {
            this.pAttrAuthcode = pAttrAuthcode;
        }

        [FieldOffset(0)]
        public SIGNER_ATTR_AUTHCODE* pAttrAuthcode;
    }

    internal enum SignerSignTimeStampFlags : uint
    {
        SIGNER_TIMESTAMP_RFC3161 = 2
    }

    internal enum SignerSignatureInfoAttrChoice : uint
    {
        SIGNER_AUTHCODE_ATTR = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIGNER_CERT
    {
        public uint cbSize;
        public SignerCertChoice dwCertChoice;
        public SIGNER_CERT_UNION union;
        public IntPtr hwnd;

        public SIGNER_CERT(SIGNER_CERT_UNION union)
        {
            cbSize = (uint)Marshal.SizeOf<SIGNER_CERT>();
            dwCertChoice = SignerCertChoice.SIGNER_CERT_STORE;
            this.union = union;
            hwnd = default;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct SIGNER_CERT_UNION
    {
        public SIGNER_CERT_UNION(SIGNER_CERT_STORE_INFO* certStoreInfo)
        {
            pSpcChainInfo = certStoreInfo;
        }

        [FieldOffset(0)]
        public SIGNER_CERT_STORE_INFO* pSpcChainInfo;
    }

    internal enum SignerCertChoice : uint
    {
        SIGNER_CERT_STORE = 2
    }

    [Flags]
    internal enum SignerSignEx3Flags : uint
    {
        SPC_EXC_PE_PAGE_HASHES_FLAG = 0x010,
        SIGN_CALLBACK_UNDOCUMENTED = 0x400
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SIGNER_SUBJECT_INFO
    {
        public uint cbSize;
        public uint* pdwIndex;
        public SignerSubjectInfoUnionChoice dwSubjectChoice;
        public SIGNER_SUBJECT_INFO_UNION unionInfo;

        public SIGNER_SUBJECT_INFO(uint* pdwIndex, SIGNER_SUBJECT_INFO_UNION unionInfo)
        {
            cbSize = (uint)Marshal.SizeOf<SIGNER_SUBJECT_INFO>();
            this.pdwIndex = pdwIndex;
            dwSubjectChoice = SignerSubjectInfoUnionChoice.SIGNER_SUBJECT_FILE;
            this.unionInfo = unionInfo;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct SIGNER_SUBJECT_INFO_UNION
    {
        [FieldOffset(0)]
        public SIGNER_FILE_INFO* file;

        public SIGNER_SUBJECT_INFO_UNION(SIGNER_FILE_INFO* file)
        {
            this.file = file;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SIGNER_FILE_INFO
    {
        public uint cbSize;
        public char* pwszFileName;
        public IntPtr hFile;

        public SIGNER_FILE_INFO(char* pwszFileName)
        {
            cbSize = (uint)Marshal.SizeOf<SIGNER_FILE_INFO>();
            this.pwszFileName = pwszFileName;
            hFile = default;
        }
    }

    internal enum SignerSubjectInfoUnionChoice : uint
    {
        SIGNER_SUBJECT_FILE = 0x01
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIGN_INFO
    {
        public uint cbSize;
        public IntPtr callback;
        public IntPtr pvOpaque;

        public SIGN_INFO(IntPtr callback)
        {
            cbSize = (uint)Marshal.SizeOf<SIGN_INFO>();
            this.callback = callback;
            pvOpaque = default;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SIGNER_ATTR_AUTHCODE
    {
        public uint cbSize;
        public uint fCommercial;
        public uint fIndividual;
        public char* pwszName;
        public char* pwszInfo;

        public SIGNER_ATTR_AUTHCODE(char* pwszName, char* pwszInfo)
        {
            cbSize = (uint)Marshal.SizeOf<SIGNER_ATTR_AUTHCODE>();
            fCommercial = 0;
            fIndividual = 0;
            this.pwszName = pwszName;
            this.pwszInfo = pwszInfo;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SIGNER_SIGN_EX3_PARAMS
    {
        public SignerSignEx3Flags dwFlags;
        public SIGNER_SUBJECT_INFO* pSubjectInfo;
        public SIGNER_CERT* pSignerCert;
        public SIGNER_SIGNATURE_INFO* pSignatureInfo;
        public IntPtr pProviderInfo;
        public SignerSignTimeStampFlags dwTimestampFlags;
        public byte* pszTimestampAlgorithmOid;
        public char* pwszHttpTimeStamp;
        public IntPtr psRequest;
        public SIGN_INFO* pSignCallBack;
        public IntPtr* ppSignerContext;
        public IntPtr pCryptoPolicy;
        public IntPtr pReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct APPX_SIP_CLIENT_DATA
    {
        public SIGNER_SIGN_EX3_PARAMS* pSignerParams;
        public IntPtr pAppxSipState;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate int SignCallback(
        IntPtr pCertContext,
        IntPtr pvExtra,
        uint algId,
        [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U1, SizeParamIndex = 4)] byte[] pDigestToSign,
        uint dwDigestToSign,
        [In, Out] ref CRYPTOAPI_BLOB blob);
}
