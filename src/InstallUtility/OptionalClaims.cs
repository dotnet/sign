using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstallUtility
{
    public class OptionalClaims
    {
        public ClaimInformation[] accessToken { get; set; }
    }

    public class ClaimInformation
    {
        public string name { get; set; }
        public string source { get; set; }
        public bool essential { get; set; }
    }

}
