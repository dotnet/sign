using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SignService.Models
{
    public class KeyVaultDetailsModel
    {
        public VaultModel Vault { get; set; }
        public List<CertificateModel> CertificateModels { get; set; }
    }
}
