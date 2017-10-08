using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Azure.KeyVault.Models;

namespace SignService.Models
{
    [DebuggerDisplay("{Name}")]
    public class CertificateModel
    {
        public string Name { get; set; }
        public string CertificateIdentifier { get; set; }
        public string Thumbprint { get; set; }
        public CertificateAttributes Attributes { get; set; }
    }
}
