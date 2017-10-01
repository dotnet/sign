using System;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
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
        readonly IHttpContextAccessor contextAccessor;
        string publisher;
        readonly string makeappxPath;

        public AppxFileFactory(ILogger<AppxFileFactory> logger, IOptions<Settings> settings, IHttpContextAccessor contextAccessor)
        {
            this.logger = logger;
            this.contextAccessor = contextAccessor;
            makeappxPath = Path.Combine(settings.Value.WinSdkBinDirectory, "makeappx.exe");
        }

        public AppxFile Create(string inputFileName)
        {
            if (publisher == null) // don't care about this race
            {
                var kv = contextAccessor.HttpContext.RequestServices.GetService<IKeyVaultService>();
                var cert = kv.GetCertificateAsync().Result;
                publisher = cert.SubjectName.Name;
            }

            return new AppxFile(inputFileName, publisher, logger, makeappxPath);
        }
    }
}
