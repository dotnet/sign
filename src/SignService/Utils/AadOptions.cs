using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SignService.Utils
{
    public class AadOptions
    {
        public string ClientId { get; set; }
        public string TenantId { get; set; }
        public string ClientSecret { get; set; }
        public string AadInstance { get; set; }
    }
}
