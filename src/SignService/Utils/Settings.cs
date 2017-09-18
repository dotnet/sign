using System;
using System.Collections.Generic;
using System.Linq;

namespace SignService
{
    public class Settings
    {
        public Settings()
        {
            certificateMap = new Lazy<Dictionary<string, CertificateInfo>>(() => CertificateMapping.ToDictionary(k => k.ObjectId));
        }

        readonly Lazy<Dictionary<string, CertificateInfo>> certificateMap;

        public List<CertificateInfo> CertificateMapping { get; set; }

        public string WinSdkBinDirectory { get; set; }

        public Dictionary<string, CertificateInfo> UserCertificateInfoMap => certificateMap.Value;
    }
}
