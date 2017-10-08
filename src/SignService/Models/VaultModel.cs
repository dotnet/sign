using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SignService.Models
{
    public class VaultModel
    {
        public string Id { get; set; }
        public string Location { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
    }
}
