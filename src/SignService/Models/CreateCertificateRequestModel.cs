using System.ComponentModel;

using Microsoft.AspNetCore.Mvc;

namespace SignService.Models
{
    public class CreateCertificateRequestModel
    {
        [HiddenInput]
        public string VaultName { get; set; }

        [DisplayName("Certificate Id in the Key Vault")]
        public string CertificateId { get; set; }

        public string CommonName { get; set; }
    }
}
