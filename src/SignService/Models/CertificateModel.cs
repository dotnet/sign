using System.Diagnostics;
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
        public CertificateOperation Operation { get; set; }
    }
}
