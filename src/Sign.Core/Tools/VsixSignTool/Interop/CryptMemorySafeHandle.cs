// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Interop
{
    internal sealed class CryptMemorySafeHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        public CryptMemorySafeHandle(bool ownsHandle) : base(ownsHandle)
        {
        }

        public CryptMemorySafeHandle() : this(true)
        {
        }

        protected override bool ReleaseHandle()
        {
            Crypt32.CryptMemFree(handle);
            return true;
        }
    }
}
