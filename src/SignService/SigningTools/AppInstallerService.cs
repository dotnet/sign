using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SignService.Services;

namespace SignService.SigningTools
{
    // Not really signing anything, but updates the manifest file with the
    // correct publisher information
    class AppInstallerService : ICodeSignService
    {
        readonly IKeyVaultService keyVaultService;
        readonly ILogger<AppInstallerService> logger;

        public AppInstallerService(IKeyVaultService keyVaultService, ILogger<AppInstallerService> logger)
        {
            this.keyVaultService = keyVaultService;
            this.logger = logger;
        }


        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new List<string>
        {
            ".appinstaller"
        };

        public bool IsDefault => false;

        public async Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files, string filter)
        {
            logger.LogInformation("Editing AppInstaller job {0} with {1} files", name, files.Count());

            var cert = await keyVaultService.GetCertificateAsync().ConfigureAwait(false);
            var publisher = cert.SubjectName.Name;

            // We need to open the files, and update the publisher value
            foreach (var file in files)
            {
                XDocument manifest;
                using (var fs = File.OpenRead(file))
                {
                    manifest = XDocument.Load(fs, LoadOptions.PreserveWhitespace);
                    XNamespace ns = "http://schemas.microsoft.com/appx/appinstaller/2017/2";

                    var idElement = manifest.Root?.Element(ns + "MainBundle");
                    idElement?.SetAttributeValue("Publisher", publisher);
                }

                using (var fs = File.Create(file))
                {
                    manifest.Save(fs);
                }
            }
        }
    }
}
