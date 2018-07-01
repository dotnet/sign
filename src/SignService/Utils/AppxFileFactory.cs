using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

        public AppxFileFactory(ILogger<AppxFileFactory> logger, IOptionsSnapshot<WindowsSdkFiles> windowSdkFiles, IKeyVaultService keyVaultService)
        {
            this.logger = logger;
            this.keyVaultService = keyVaultService;
            makeappxPath = windowSdkFiles.Value.MakeAppxPath;
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
