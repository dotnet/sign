using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SignService.Models
{
    public class UserDetailsModel
    {
        public GraphUser User { get; set; }
        public string VaultName { get; set; }
        public bool HasVaultName => !string.IsNullOrWhiteSpace(VaultName);
    }

}
