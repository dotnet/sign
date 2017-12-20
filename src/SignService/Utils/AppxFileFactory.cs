using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using SignService.Services;

namespace SignService.Utils
{
    public interface IAppxFileFactory
    {
        AppxFile Create(string inputFileName);
    }

    public class AppxFileFactory : IAppxFileFactory
    {
        readonly ILogger<AppxFileFactory> logger;
        readonly IKeyVaultService keyVaultService;
        string publisher;
        readonly string makeappxPath;

        public AppxFileFactory(ILogger<AppxFileFactory> logger, IHostingEnvironment hostingEnvironment, IKeyVaultService keyVaultService)
        {
            this.logger = logger;
            this.keyVaultService = keyVaultService;
            makeappxPath = Path.Combine(hostingEnvironment.ContentRootPath, "tools\\SDK\\makeappx.exe");
        }

        public AppxFile Create(string inputFileName)
        {
            if (publisher == null) // don't care about this race
            {
                var cert = keyVaultService.GetCertificateAsync().Result;
                publisher = cert.SubjectName.Name;
            }

            return new AppxFile(inputFileName, publisher, logger, makeappxPath);
        }
    }
}
