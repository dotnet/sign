using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SignService
{
    public class AdminConfig
    {
        public string SubscriptionId { get; set; }
        public string ResourceGroup { get; set; }
        public string ArmInstance { get; set; }
        public string GraphInstance { get; set; }
    }
}
