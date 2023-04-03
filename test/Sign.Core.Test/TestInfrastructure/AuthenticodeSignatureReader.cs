// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography.Pkcs;

namespace Sign.Core.Test
{
    internal static class AuthenticodeSignatureReader
    {
        private const string Crypt32Dll = "Crypt32.dll";
        private const int CERT_QUERY_OBJECT_FILE = 0x00000001;
        private const int CERT_QUERY_CONTENT_PKCS7_SIGNED_EMBED = 10;
        private const int CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED = (1 << CERT_QUERY_CONTENT_PKCS7_SIGNED_EMBED);
        private const int CERT_QUERY_FORMAT_BINARY = 1;
        private const int CERT_QUERY_FORMAT_FLAG_BINARY = (1 << CERT_QUERY_FORMAT_BINARY);
        private const int CMSG_ENCODED_MESSAGE = 29;

        internal static bool TryGetSignedCms(FileInfo file, [NotNullWhen(true)] out SignedCms? signedCms)
        {
            signedCms = null;

            byte[] blob = GetSignedCmsBlob(file);

            if (blob is not null && blob.Length > 0)
            {
                SignedCms cms = new();

                cms.Decode(blob);

                signedCms = cms;
            }

            return signedCms is not null;
        }

        private static byte[] GetSignedCmsBlob(FileInfo file)
        {
            IntPtr pvObject = Marshal.StringToHGlobalUni(file.FullName);
            IntPtr phCertStore = IntPtr.Zero;
            IntPtr phMsg = IntPtr.Zero;
            byte[]? pvData = null;

            try
            {
                IntPtr ppvContext = IntPtr.Zero;

                if (!CryptQueryObject(
                    CERT_QUERY_OBJECT_FILE,
                    pvObject,
                    CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED,
                    CERT_QUERY_FORMAT_FLAG_BINARY,
                    dwFlags: 0,
                    out int pdwMsgAndCertEncodingType,
                    out int pdwContentType,
                    out int pdwFormatType,
                    ref phCertStore,
                    ref phMsg,
                    ref ppvContext))
                {
                    throw new Win32Exception();
                }

                int pcbData = 0;

                if (!CryptMsgGetParam(phMsg, CMSG_ENCODED_MESSAGE, dwIndex: 0, IntPtr.Zero, ref pcbData))
                {
                    throw new Win32Exception();
                }

                pvData = new byte[pcbData];

                if (!CryptMsgGetParam(phMsg, CMSG_ENCODED_MESSAGE, dwIndex: 0, pvData, ref pcbData))
                {
                    throw new Win32Exception();
                }
            }
            finally
            {
                if (phMsg != IntPtr.Zero)
                {
                    CryptMsgClose(phMsg);
                }

                if (phCertStore != IntPtr.Zero)
                {
                    CertCloseStore(phCertStore, dwFlags: 0);
                }

                if (pvObject != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pvObject);
                }
            }

            return pvData;
        }

        [DllImport(Crypt32Dll, CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool CertCloseStore(
            /* HCERTSTORE */ [In] IntPtr hCertStore,
            /* DWORD */ [In] int dwFlags);

        [DllImport(Crypt32Dll, CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool CryptMsgClose(
            /* HCRYPTMSG */ [In] IntPtr hCryptMsg);

        [DllImport(Crypt32Dll, CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool CryptQueryObject(
            /* DWORD */ [In] int dwObjectType,
            /* const void* */ [In] IntPtr pvObject,
            /* DWORD */ [In] int dwExpectedContentTypeFlags,
            /* DWORD */ [In] int dwExpectedFormatTypeFlags,
            /* DWORD */ [In] int dwFlags,
            /* DWORD* */ [Out] out int pdwMsgAndCertEncodingType,
            /* DWORD* */ [Out] out int pdwContentType,
            /* DWORD* */ [Out] out int pdwFormatType,
            /* HCERTSTORE* */ ref IntPtr phCertStore,
            /* HCRYPTMSG* */ ref IntPtr phMsg,
            /* const void** */ ref IntPtr ppvContext);

        [DllImport(Crypt32Dll, CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool CryptMsgGetParam(
            /* HCRYPTMSG */ [In] IntPtr hCryptMsg,
            /* DWORD */ [In] int dwParamType,
            /* DWORD */ [In] int dwIndex,
            /* void* */ [In, Out] IntPtr pvData,
            /* DWORD* */ [In, Out] ref int pcbData);

        [DllImport(Crypt32Dll, CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool CryptMsgGetParam(
            /* HCRYPTMSG */ [In] IntPtr hCryptMsg,
            /* DWORD */ [In] int dwParamType,
            /* DWORD */ [In] int dwIndex,
            /* void* */ [In, Out] byte[] pvData,
            /* DWORD* */ [In, Out] ref int pcbData);
    }
}