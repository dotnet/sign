using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SignService.Models
{
    public class UpdateCertificateRequestModel
    {
        [HiddenInput]
        public string VaultName { get; set; }

        [HiddenInput]
        public string CertificateName { get; set; }

        [HiddenInput]
        public string Csr { get; set; }

        [Required]
        public IFormFile Certificate { get; set; }
    }
}
