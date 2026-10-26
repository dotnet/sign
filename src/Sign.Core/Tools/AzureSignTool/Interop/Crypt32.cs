// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Runtime.InteropServices;

namespace Sign.Core.Interop
{
    internal static partial class Crypt32
    {
        [DllImport("crypt32.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CertCloseStore(IntPtr hCertStore, uint dwFlags);

        [DllImport("crypt32.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        public static extern IntPtr CertOpenStore(
            [MarshalAs(UnmanagedType.LPStr)] string lpszStoreProvider,
            uint dwMsgAndCertEncodingType,
            IntPtr hCryptProv,
            uint dwFlags,
            IntPtr pvPara);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIGNER_CERT_STORE_INFO(IntPtr pSigningCert, SignerCertStoreInfoFlags dwCertPolicy, IntPtr hCertStore)
    {
        public uint cbSize = (uint)Marshal.SizeOf<SIGNER_CERT_STORE_INFO>();
        public IntPtr pSigningCert = pSigningCert;
        public SignerCertStoreInfoFlags dwCertPolicy = dwCertPolicy;
        public IntPtr hCertStore = hCertStore;
    }

    internal enum SignerCertStoreInfoFlags : uint
    {
        SIGNER_CERT_POLICY_CHAIN = 0x02
    }
}
