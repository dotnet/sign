using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace SignService.Utils
{
    public static class CryptoUtil
    {
        public static X509Certificate2Collection GetCertificatesFromCryptoData(byte[] data)
        {
            // the data we expect is base64-encoded string data that starts with either
            // a PKCS7 or CERTIFICATE header 
            using (var sr = new StreamReader(new MemoryStream(data)))
            {
                // Read the first line to detect which type of data we're dealing with

                var firstLine = sr.ReadLine();

                X509Certificate2 cert = null;

                if (firstLine.Contains("PKCS7"))
                {
                    cert = CertificateFromPkcs7Data(data);
                }
                else if (firstLine.Contains("CERTIFICATE"))
                {
                    cert = CertificateFromCerData(data);
                }
                else
                {
                    throw new ArgumentException("File does not have either a PKCS7 or CERTIFICATE header", nameof(data));
                }

                return new X509Certificate2Collection(cert);
            }
        }

        static X509Certificate2 CertificateFromCerData(byte[] data)
        {
            return new X509Certificate2(data);
        }

        static X509Certificate2 CertificateFromPkcs7Data(byte[] data)
        {
            // Strip off the header/footer
            using (var sr = new StreamReader(new MemoryStream(data)))
            {
                var lines = new List<string>();
                while (!sr.EndOfStream)
                {
                    var line = sr.ReadLine();
                    if (!line.StartsWith("-----"))
                    {
                        lines.Add(line);
                    }
                }
                var certLines = string.Join("", lines);
                data = Convert.FromBase64String(certLines);
            }

            var depth = -1;
            X509Certificate2 bestCertificate = null;
            using (var store = Pkcs7CertificateStore.Create(data))
            {
                using (var chain = new X509Chain())
                {
                    chain.ChainPolicy.ExtraStore.AddRange(store.Certificates);
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
                    foreach (var cert in store.Certificates)
                    {
                        chain.Build(cert);
                        if (chain.ChainElements.Count > depth)
                        {
                            bestCertificate = cert;
                            depth = chain.ChainElements.Count;
                        }
                    }
                }
            }

            if (bestCertificate == null)
            {
                throw new ArgumentException("No certificates found in the file", nameof(data));
            }

            return bestCertificate;
        }
    }
}
