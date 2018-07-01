using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SignService.Utils
{
    public static class Crypt32
    {
        /// <summary>
        /// Turns a binary crypt string into a response. 
        /// </summary>
        /// <param name="binary"></param>
        /// <param name="isRequest">True for a CSR, False for a certificate</param>
        /// <param name="forDisplay">True for use in a web page (uses NOCR), false for outputting to a file</param>
        /// <returns></returns>
        public static string CryptBinaryToString(byte[] binary, bool isRequest, bool forDisplay)
        {
            var flags = isRequest ? CryptBinaryToStringFlags.CRYPT_STRING_BASE64REQUESTHEADER : CryptBinaryToStringFlags.CRYPT_STRING_BASE64HEADER;
            if (forDisplay)
            {
                flags = flags | CryptBinaryToStringFlags.CRYPT_STRING_NOCR;
            }

            uint size = 0;
            if (CryptBinaryToString(binary, (uint)binary.Length, flags, null, ref size))
            {
                var builder = new StringBuilder((int)size);
                if (CryptBinaryToString(binary, (uint)binary.Length, flags, builder, ref size))
                {
                    return builder.ToString();
                }
            }

            var hr = Marshal.GetHRForLastWin32Error();
            Marshal.ThrowExceptionForHR(hr);
            return null;
        }


        [method: DllImport("crypt32.dll", CallingConvention = CallingConvention.Winapi, EntryPoint = "CryptBinaryToString", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern unsafe bool CryptBinaryToString
        (
            [param: In] byte[] pbBinary,
            [param: In, MarshalAs(UnmanagedType.U4)] uint cbBinary,
            [param: In, MarshalAs(UnmanagedType.U4)] CryptBinaryToStringFlags dwFlags,
            [param: In, Out] StringBuilder pszString,
            [param: In, Out] ref uint pcchString
        );

        [return: MarshalAs(UnmanagedType.Bool)]
        [method: DllImport(nameof(Crypt32), CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        public static extern bool CertCloseStore
        (
            [In, MarshalAs(UnmanagedType.SysInt)] IntPtr hCertStore,
            [In, MarshalAs(UnmanagedType.U4)] CertCloreStoreFlags dwFlags
        );

        [return: MarshalAs(UnmanagedType.SysInt)]
        [method: DllImport(nameof(Crypt32), CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        public static extern IntPtr CertOpenStore
        (
            [In, MarshalAs(UnmanagedType.LPStr)] string lpszStoreProvider,
            [In, MarshalAs(UnmanagedType.U4)] CertEncodingType CertEncodingType,
            [In, MarshalAs(UnmanagedType.SysInt)] IntPtr hCryptProv,
            [In, MarshalAs(UnmanagedType.U4)] CertOpenStoreFlags dwFlags,
            [In] ref CRYPTOAPI_BLOB pvPara
        );

        internal enum CryptBinaryToStringFlags : uint
        {
            CRYPT_STRING_BASE64HEADER = 0x00000000,
            CRYPT_STRING_BASE64 = 0x00000001,
            CRYPT_STRING_BINARY = 0x00000002,
            CRYPT_STRING_BASE64REQUESTHEADER = 0x00000003,
            CRYPT_STRING_HEX = 0x00000004,
            CRYPT_STRING_HEXASCII = 0x00000005,
            CRYPT_STRING_BASE64X509CRLHEADER = 0x00000009,
            CRYPT_STRING_HEXADDR = 0x0000000a,
            CRYPT_STRING_HEXASCIIADDR = 0x0000000b,
            CRYPT_STRING_HEXRAW = 0x0000000c,
            CRYPT_STRING_STRICT = 0x20000000,
            CRYPT_STRING_NOCRLF = 0x40000000,
            CRYPT_STRING_NOCR = 0x80000000,
        }

        [type: Flags]
        internal enum SignerCertStoreInfoFlags
        {

            SIGNER_CERT_POLICY_CHAIN = 0x02,
            SIGNER_CERT_POLICY_CHAIN_NO_ROOT = 0x08,
            SIGNER_CERT_POLICY_STORE = 0x01
        }

        [type: Flags]
        public enum CertOpenStoreFlags : uint
        {
            NONE = 0,
            CERT_STORE_NO_CRYPT_RELEASE_FLAG = 0x00000001,
            CERT_STORE_SET_LOCALIZED_NAME_FLAG = 0x00000002,
            CERT_STORE_DEFER_CLOSE_UNTIL_LAST_FREE_FLAG = 0x00000004,
            CERT_STORE_DELETE_FLAG = 0x00000010,
            CERT_STORE_UNSAFE_PHYSICAL_FLAG = 0x00000020,
            CERT_STORE_SHARE_STORE_FLAG = 0x00000040,
            CERT_STORE_SHARE_CONTEXT_FLAG = 0x00000080,
            CERT_STORE_MANIFOLD_FLAG = 0x00000100,
            CERT_STORE_ENUM_ARCHIVED_FLAG = 0x00000200,
            CERT_STORE_UPDATE_KEYID_FLAG = 0x00000400,
            CERT_STORE_BACKUP_RESTORE_FLAG = 0x00000800,
            CERT_STORE_READONLY_FLAG = 0x00008000,
            CERT_STORE_OPEN_EXISTING_FLAG = 0x00004000,
            CERT_STORE_CREATE_NEW_FLAG = 0x00002000,
            CERT_STORE_MAXIMUM_ALLOWED_FLAG = 0x00001000,
        }

        [type: Flags]
        public enum CertCloreStoreFlags : uint
        {
            NONE = 0,
            CERT_CLOSE_STORE_FORCE_FLAG = 0x00000001,
            CERT_CLOSE_STORE_CHECK_FLAG = 0x00000002,
        }

        public enum CertEncodingType : uint
        {
            NONE = 0,
            X509_ASN_ENCODING = 0x1,
            PKCS_7_ASN_ENCODING = 0x10000
        }

        [type: StructLayout(LayoutKind.Sequential)]
        public struct CRYPTOAPI_BLOB
        {
            public uint cbData;
            public IntPtr pbData;
        }
    }

    public sealed class Pkcs7CertificateStore : IDisposable
    {
        IntPtr _handle;
        X509Store _store;
        Crypt32.CRYPTOAPI_BLOB _blob;

        Pkcs7CertificateStore(IntPtr handle, Crypt32.CRYPTOAPI_BLOB blob)
        {
            _handle = handle;
            try
            {
                _blob = blob;
                _store = new X509Store(_handle);
            }
            catch
            {
                //We need to manually clean up the handle here. If we throw here for whatever reason,
                //we'll leak the handle because we'll have a partially constructed object that won't get
                //a finalizer called on or anything to dispose of.
                FreeHandle();
                throw;
            }
        }

        public static Pkcs7CertificateStore Create(byte[] data)
        {
            const string STORE_TYPE = "PKCS7";
            var dataPtr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, dataPtr, data.Length);
            var blob = new Crypt32.CRYPTOAPI_BLOB
            {
                cbData = (uint)data.Length,
                pbData = dataPtr
            };
            var handle = Crypt32.CertOpenStore(STORE_TYPE, Crypt32.CertEncodingType.NONE, IntPtr.Zero, Crypt32.CertOpenStoreFlags.NONE, ref blob);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create a memory certificate store.");
            }
            return new Pkcs7CertificateStore(handle, blob);
        }

        public void Close() => Dispose(true);
        void IDisposable.Dispose() => Dispose(true);
        ~Pkcs7CertificateStore() => Dispose(false);

        public IntPtr Handle => _store.StoreHandle;
        public void Add(X509Certificate2 certificate) => _store.Add(certificate);
        public void Add(X509Certificate2Collection collection) => _store.AddRange(collection);
        public X509Certificate2Collection Certificates => _store.Certificates;

        void Dispose(bool disposing)
        {
            GC.SuppressFinalize(this);
            if (disposing)
            {
                _store.Dispose();
            }
            FreeHandle();
        }

        void FreeHandle()
        {
            if (_handle != IntPtr.Zero)
            {
                var closed = Crypt32.CertCloseStore(_handle, Crypt32.CertCloreStoreFlags.NONE);
                _handle = IntPtr.Zero;
                Debug.Assert(closed);
            }
            Marshal.FreeHGlobal(_blob.pbData);
        }
    }

}
