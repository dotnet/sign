using Microsoft.AspNetCore.Mvc;

namespace SignService.Models
{
    public class CreateCertificateRequestModel
    {
        [HiddenInput]
        public string VaultName { get; set; }
        public string CertificateName { get; set; }
    }
}
