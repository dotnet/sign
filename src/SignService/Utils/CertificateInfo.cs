using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SignService
{
    public class CertificateInfo
    {
        public string TimestampUrl { get; set; }
        public string KeyVaultUrl { get; set; }
        public string CertificateName { get; set; }
    }
}
