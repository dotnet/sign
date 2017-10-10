using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
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
