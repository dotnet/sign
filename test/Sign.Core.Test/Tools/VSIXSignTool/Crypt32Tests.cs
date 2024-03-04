// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Sign.Core.Interop;
using System.Runtime.InteropServices;

namespace Sign.Core.Test
{
    public class Crypt32Tests
    {
        [Fact]
        public void ShouldTimestampData()
        {
            var data = new byte[] { 1, 2, 3 };
            var parameters = new CRYPT_TIMESTAMP_PARA
            {
                cExtension = 0,
                fRequestCerts = true,
                pszTSAPolicyId = null
            };

            var ok = Crypt32.CryptRetrieveTimeStamp("http://timestamp.digicert.com", CryptRetrieveTimeStampRetrievalFlags.NONE, 30 * 1000, "1.3.14.3.2.26", ref parameters, data, (uint)data.Length, out var pointer, IntPtr.Zero, IntPtr.Zero);
            Assert.True(ok);
            bool success = false;
            try
            {
                pointer.DangerousAddRef(ref success);
                Assert.True(success);
                var structure = Marshal.PtrToStructure<CRYPT_TIMESTAMP_CONTEXT>(pointer.DangerousGetHandle());
                var encoded = new byte[structure.cbEncoded];
                Marshal.Copy(structure.pbEncoded, encoded, 0, encoded.Length);
            }
            finally
            {
                if (success)
                {
                    pointer.DangerousRelease();
                }
            }
        }
    }
}
