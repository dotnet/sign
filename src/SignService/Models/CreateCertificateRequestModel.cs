using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
