// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Runtime.InteropServices;

namespace Sign.Core.Interop
{
    internal static class Crypt32
    {
        [method: DllImport("crypt32.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern void CryptMemFree(
            [param: In, MarshalAs(UnmanagedType.SysInt)] IntPtr pv
        );

        [method: DllImport("crypt32.dll", CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CryptRetrieveTimeStamp(
            [param: In, MarshalAs(UnmanagedType.LPWStr)] string wszUrl,
            [param: In, MarshalAs(UnmanagedType.U4)] CryptRetrieveTimeStampRetrievalFlags dwRetrievalFlags,
            [param: In, MarshalAs(UnmanagedType.U4)] uint dwTimeout,
            [param: In, MarshalAs(UnmanagedType.LPStr)] string? pszHashId,
            [param: In] ref CRYPT_TIMESTAMP_PARA pPara,
            [param: In, MarshalAs(UnmanagedType.LPArray)] byte[] pbData,
            [param: In, MarshalAs(UnmanagedType.U4)] uint cbData,
            [param: Out] out CryptMemorySafeHandle ppTsContext,
            [param: In, MarshalAs(UnmanagedType.SysInt)] IntPtr ppTsSigner,
            [param: In, MarshalAs(UnmanagedType.SysInt)] IntPtr phStore
        );
    }

    internal enum CryptRetrieveTimeStampRetrievalFlags : uint
    {
        NONE = 0x0,
        TIMESTAMP_DONT_HASH_DATA = 0x01,
        TIMESTAMP_VERIFY_CONTEXT_SIGNATURE = 0x00000020,
        TIMESTAMP_NO_AUTH_RETRIEVAL = 0x00020000
    }

    [type: StructLayout(LayoutKind.Sequential)]
    internal struct CRYPT_TIMESTAMP_PARA
    {
        public string? pszTSAPolicyId;
        public bool fRequestCerts;
        public CRYPTOAPI_BLOB Nonce;
        public uint cExtension;
        public IntPtr rgExtension;
    }

    [type: StructLayout(LayoutKind.Sequential)]
    internal struct CRYPTOAPI_BLOB
    {
        public uint cbData;
        public IntPtr pbData;
    }

    [type: StructLayout(LayoutKind.Sequential)]
    internal struct CRYPT_TIMESTAMP_CONTEXT
    {
        public uint cbEncoded;
        public IntPtr pbEncoded;
        public IntPtr pTimeStamp;
    }
}
