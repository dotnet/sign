// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography.X509Certificates;

namespace Sign.Core.Test
{
    internal static class TestUtility
    {
        internal static void RemoveTestIntermediateCertificates()
        {
            // See https://github.com/dotnet/runtime/blob/07d8b82d54b6b8db16f5fbb531efcb1e276dc264/src/libraries/System.Security.Cryptography.X509Certificates/tests/RevocationTests/AiaTests.cs#L99-L122
            using (X509Store store = new(StoreName.CertificateAuthority, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadWrite);

                foreach (X509Certificate2 storeCert in store.Certificates)
                {
                    if (storeCert.Extensions[Oids.Test.Value!] is not null)
                    {
                        store.Remove(storeCert);
                    }

                    storeCert.Dispose();
                }
            }
        }
    }
}