// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Sign.TestInfrastructure
{
    public sealed class EphemeralTrust : IDisposable
    {
        private readonly X509Certificate2 _certificate;

        [SupportedOSPlatform("windows")]
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

        // If test certificates from a previous test run are still trusted,
        // we need to remove them if we can or have them removed by the developer before continuing.
        public static void RemoveResidualTestCertificates()
        {
            using (X509Store store = GetStore())
            {
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

                X509Certificate2Collection certificates = store.Certificates;
                List<X509Certificate2> oldCertificates = new();

                foreach (X509Certificate2 certificate in certificates)
                {
                    if (string.Equals(certificate.FriendlyName, Constants.FriendlyName))
                    {
                        oldCertificates.Add(certificate);
                    }
                }

                store.Close();

                if (oldCertificates.Count > 0)
                {
                    if (Environment.IsPrivilegedProcess)
                    {
                        store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);

                        try
                        {
                            foreach (X509Certificate2 certificate in oldCertificates)
                            {
                                store.Remove(certificate);
                            }
                        }
                        finally
                        {
                            store.Close();
                        }
                    }
                    else
                    {
                        StringBuilder messageBuilder = new();
                        bool isSingular = oldCertificates.Count == 1;

                        messageBuilder.Append($"{oldCertificates.Count} certificate{(isSingular ? string.Empty : "s")} from a previous test run {(isSingular ? "was" : "were")} found ");
                        messageBuilder.Append("in the local machine's \"Trusted Root Certification Authorities\" store.  ");
                        messageBuilder.Append($"Please remove the following certificate{(isSingular ? string.Empty : "s")} manually and rerun.  ");
                        messageBuilder.Append($"All test certificates have a \"Friendly Name\" value of {Constants.FriendlyName}.");
                        messageBuilder.AppendLine();

                        foreach (X509Certificate2 certificate in oldCertificates)
                        {
                            messageBuilder.AppendLine($"  Subject:  {certificate.Subject}");
                            messageBuilder.AppendLine($"    Friendly name:  {certificate.FriendlyName}");
                        }

                        throw new ResidualTestCertificatesFoundInRootStoreException(messageBuilder.ToString());
                    }
                }
            }
        }

        [SupportedOSPlatform("windows")]
        private void AddTrust()
        {
            // This enables us to easily and reliably identify our test certificates.
            _certificate.FriendlyName = Constants.FriendlyName;

            using (X509Store store = GetStore())
            {
                store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);

                // CodeQL [SM02730] This is test code. This adds a short-lived test certificate to the root store for testing signing and signature verification. The certificate is later removed. See internal bug 2292291.
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
