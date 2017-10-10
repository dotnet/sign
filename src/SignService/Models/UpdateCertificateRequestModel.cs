using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace SignService.Models
{
    public class UpdateCertificateRequestModel
    {
        [HiddenInput]
        public string VaultName { get; set; }

        [HiddenInput]
        public string CertificateName { get; set; }

        public string Csr { get; set; }
    }
}
