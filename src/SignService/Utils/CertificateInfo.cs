using System;

namespace SignService
{
    public class CertificateInfo
    {
        public string TimestampUrl { get; set; }
        public Uri KeyVaultUrl { get; set; }
        public string CertificateName { get; set; }
    }
}
