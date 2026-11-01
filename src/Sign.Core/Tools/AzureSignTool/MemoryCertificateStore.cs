// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Sign.Core.Interop;

namespace Sign.Core
{
    internal sealed class MemoryCertificateStore : IDisposable
    {
        private IntPtr _handle;
        private readonly X509Store _store;

        private MemoryCertificateStore(IntPtr handle)
        {
            _handle = handle;
            try
            {
                _store = new X509Store(_handle);
            }
            catch
            {
                FreeHandle();
                throw;
            }
        }

        public static MemoryCertificateStore Create()
        {
            IntPtr handle = Crypt32.CertOpenStore("Memory", 0, IntPtr.Zero, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create a memory certificate store.");
            }

            return new MemoryCertificateStore(handle);
        }

        ~MemoryCertificateStore() => Dispose(false);

        public IntPtr Handle => _store.StoreHandle;

        public void Add(X509Certificate2 certificate) => _store.Add(certificate);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                _store.Dispose();
            }

            FreeHandle();
        }

        private void FreeHandle()
        {
            if (_handle != IntPtr.Zero)
            {
                bool closed = Crypt32.CertCloseStore(_handle, 0);
                _handle = IntPtr.Zero;
                Debug.Assert(closed);
            }
        }
    }
}
