using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace SignService.Utils
{
    public interface IAppxFileFactory
    {
        AppxFile Create(string inputFileName);
    }

    public class AppxFileFactory : IAppxFileFactory
    {
        readonly ILogger<AppxFileFactory> logger;
        readonly IOptions<Settings> settings;
        readonly IHttpContextAccessor contextAccessor;
        string publisher;
        readonly string makeappxPath;

        public AppxFileFactory(ILogger<AppxFileFactory> logger, IOptions<Settings> settings, IHttpContextAccessor contextAccessor)
        {
            this.logger = logger;
            this.settings = settings;
            this.contextAccessor = contextAccessor;
            makeappxPath = Path.Combine(settings.Value.WinSdkBinDirectory, "makeappx.exe");
        }

        public AppxFile Create(string inputFileName)
        {
            if (publisher == null) // don't care about this race
            {
                if (settings.Value.CertificateInfo.UseKeyVault)
                {
                    var kv = contextAccessor.HttpContext.RequestServices.GetService<IKeyVaultService>();
                    var cert = kv.GetCertificateAsync().Result;
                    publisher = cert.SubjectName.Name;
                }
                else
                {
                    var thumbprint = settings.Value.CertificateInfo.Thumbprint;
                    var cert = FindCertificate(thumbprint, StoreLocation.CurrentUser) ?? FindCertificate(thumbprint, StoreLocation.LocalMachine);
                    publisher = cert.SubjectName.Name;
                }
                
            }

            return new AppxFile(inputFileName, publisher, logger, makeappxPath);
        }

        static X509Certificate2 FindCertificate(string thumbprint, StoreLocation location)
        {
            using (var store = new X509Store(StoreName.My, location))
            {
                store.Open(OpenFlags.ReadOnly);
                var certs = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, true);
                if (certs.Count == 0)
                    return null;

                return certs[0];
            }
        }
    }
}
