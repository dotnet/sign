using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

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
        static unsafe extern bool CryptBinaryToString
        (
            [param: In] byte[] pbBinary,
            [param: In, MarshalAs(UnmanagedType.U4)] uint cbBinary,
            [param: In, MarshalAs(UnmanagedType.U4)] CryptBinaryToStringFlags dwFlags,
            [param: In, Out] StringBuilder pszString,
            [param: In, Out] ref uint pcchString
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

    }
}
