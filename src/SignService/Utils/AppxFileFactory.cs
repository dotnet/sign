﻿using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignService.Services;

namespace SignService.Utils
{
    public interface IAppxFileFactory
    {
        Task<AppxFile> Create(string inputFileName, string filter);
    }

    public class AppxFileFactory : IAppxFileFactory
    {
        readonly ILogger<AppxFileFactory> logger;
        readonly IKeyVaultService keyVaultService;
        readonly IDirectoryUtility directoryUtility;
        string publisher;
        readonly string makeappxPath;

        public AppxFileFactory(ILogger<AppxFileFactory> logger,
                               IOptionsSnapshot<WindowsSdkFiles> windowSdkFiles,
                               IKeyVaultService keyVaultService,
                               IDirectoryUtility directoryUtility)
        {
            this.logger = logger;
            this.keyVaultService = keyVaultService;
            this.directoryUtility = directoryUtility;
            makeappxPath = windowSdkFiles.Value.MakeAppxPath;
        }

        public async Task<AppxFile> Create(string inputFileName, string filter)
        {
            if (publisher == null) // don't care about this race
            {
                var cert = await keyVaultService.GetCertificateAsync();
                publisher = cert.SubjectName.Name;
            }

            return new AppxFile(inputFileName, publisher, logger, directoryUtility, makeappxPath, filter);
        }
    }
}
