using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SignService.Models
{
    public class VaultModel
    {
        public string VaultUri { get; set; }
        public string Location { get; set; }
        public string Name { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
    }
}
