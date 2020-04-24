using System;
using System.Diagnostics;

using Azure.Security.KeyVault.Certificates;



namespace SignService.Models
{
    [DebuggerDisplay("{Name}")]
    public class CertificateModel
    {
        public string Name { get; set; }
        public Uri CertificateIdentifier { get; set; }
        public string Thumbprint { get; set; }
        public CertificateProperties Attributes { get; set; }
        public CertificateOperation Operation { get; set; }
    }
}
