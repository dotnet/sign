using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SignService.Utils
{
    public class AdminConfig
    {
        public string SubscriptionId { get; set; }
        public string ResourceGroup { get; set; }
        public string Location { get; set; }
        public string GraphInstance { get; set; }
    }
}
