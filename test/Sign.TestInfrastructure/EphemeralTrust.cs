// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography.X509Certificates;

namespace Sign.TestInfrastructure
{
    internal sealed class EphemeralTrust : IDisposable
    {
        private readonly X509Certificate2 _certificate;

        internal EphemeralTrust(X509Certificate2 certificate)
        {
            ArgumentNullException.ThrowIfNull(certificate, nameof(certificate));

            // Note: This class does not assume ownership of the certificate.
            _certificate = certificate;

            AddTrust();
        }

        public void Dispose()
        {
            RemoveTrust();

            // Do not dispose of _certificate, as this class did not assume ownership of it.
        }

        private void AddTrust()
        {
            using (X509Store store = GetStore())
            {
                store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);

                store.Add(_certificate);

                store.Close();
            }
        }

        private void RemoveTrust()
        {
            using (X509Store store = GetStore())
            {
                store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);

                store.Remove(_certificate);

                store.Close();
            }
        }

        private static X509Store GetStore()
        {
            // StoreName.Root is necessary for trust.
            // StoreLocation.LocalMachine does not pop UI confirmation like StoreLocation.CurrentUser does.
            return new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        }
    }
}
