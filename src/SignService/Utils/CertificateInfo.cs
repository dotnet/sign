using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SignService
{
    public class CertificateInfo
    {
        public string Thumbprint { get; set; }
        public string TimestampUrl { get; set; }
        public string KeyVaultUrl { get; set; }
        public string KeyVaultCertificateName { get; set; }
    }
}
