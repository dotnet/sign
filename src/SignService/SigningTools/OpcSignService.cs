using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SignService.SigningTools
{
    public class OpcSignService : ICodeSignService
    {
        readonly string timeStampUrl;
        readonly string thumbprint;
        readonly X509Certificate2 signingCert;

        public OpcSignService(string timeStampUrl, string thumbprint, ILogger<OpcSignService> logger)
        {
            this.timeStampUrl = timeStampUrl;
            this.thumbprint = thumbprint;


        }


        public Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files)
        {
            return Task.CompletedTask;    
        }



        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new List<string>
        {
            ".docm",
            ".docx"
        };
        public bool IsDefault { get; }
    }
}
