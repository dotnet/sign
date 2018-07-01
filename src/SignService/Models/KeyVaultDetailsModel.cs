using System.Collections.Generic;

namespace SignService.Models
{
    public class KeyVaultDetailsModel
    {
        public VaultModel Vault { get; set; }
        public List<CertificateModel> CertificateModels { get; set; }
    }
}
