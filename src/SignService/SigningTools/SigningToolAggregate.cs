using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignService.Utils;

namespace SignService.SigningTools
{
    public interface ISigningToolAggregate
    {
        Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files, string filter);
    }

    public class SigningToolAggregate : ISigningToolAggregate
    {
        readonly IAppxFileFactory appxFileFactory;
        readonly ILogger<SigningToolAggregate> logger;
        readonly ICodeSignService defaultCodeSignService;
        readonly IDictionary<string, ICodeSignService> codeSignServices;
        readonly string makeappxPath;


        public SigningToolAggregate(IEnumerable<ICodeSignService> services,
                                    IAppxFileFactory appxFileFactory,
                                    IOptionsSnapshot<WindowsSdkFiles> windowSdkFiles,
                                    ILogger<SigningToolAggregate> logger)
        {
            this.appxFileFactory = appxFileFactory;
            this.logger = logger;
            makeappxPath = windowSdkFiles.Value.MakeAppxPath;

            // pe files
            defaultCodeSignService = services.Single(c => c.IsDefault);

            var list = from cs in services
                       from ext in cs.SupportedFileExtensions
                       select new { cs, ext };

            codeSignServices = list.ToDictionary(k => k.ext.ToLowerInvariant(), v => v.cs);
        }



        public async Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files, string filter)
        {
            // See if any of them are archives
            var archives = (from file in files
                            let ext = Path.GetExtension(file).ToLowerInvariant()
                            where ext == ".zip" || ext == ".nupkg" || ext == ".snupkg" || ext == ".vsix" || ext == ".appxupload" || ext == ".msixupload"
                            select file).ToList();

            // expand the archives and sign recursively first
            var tempZips = new List<TemporaryZipFile>();
            try
            {
                foreach (var archive in archives)
                {
                    tempZips.Add(new TemporaryZipFile(archive, filter, logger));
                }

                // See if there's any files in the expanded zip that we need to sign
                var allFiles = tempZips.SelectMany(tz => tz.FilteredFilesInDirectory).ToList();
                if (allFiles.Count > 0)
                {
                    // Send the files from the archives through the aggregator to sign
                    await Submit(hashMode, name, description, descriptionUrl, allFiles, filter);

                    // After signing the contents, save the zip
                    // For NuPkg, this step removes the signature too, but that's ok as it'll get signed below
                    tempZips.ForEach(tz => tz.Save());
                }
            }
            finally
            {
                tempZips.ForEach(tz => tz.Dispose());
                tempZips.Clear();
            }

            // See if there's any appx's in here, process them recursively first to sign the inner files
            var appxs = (from file in files
                         let ext = Path.GetExtension(file).ToLowerInvariant()
                         where ext == ".appx" || ext == ".eappx" || ext == ".msix" || ext == ".emsix"
                         select file).ToList();


            // See if there's any appxbundles here, process them recursively first
            // expand the archives and sign recursively first
            // This will also update the publisher information to get it ready for signing
            var tempAppxs = new List<AppxFile>();
            try
            {
                foreach (var appx in appxs)
                {
                    tempAppxs.Add(await appxFileFactory.Create(appx, filter));
                }

                // See if there's any files in the expanded zip that we need to sign
                var allFiles = tempAppxs.SelectMany(tz => tz.FilteredFilesInDirectory).ToList();
                if (allFiles.Count > 0)
                {
                    // Send the files from the archives through the aggregator to sign
                    await Submit(hashMode, name, description, descriptionUrl, allFiles, filter);
                }

                // Save the appx with the updated publisher info
                tempAppxs.ForEach(tz => tz.Save());
            }
            finally
            {
                tempAppxs.ForEach(tz => tz.Dispose());
                tempAppxs.Clear();
            }



            var bundles = (from file in files
                           let ext = Path.GetExtension(file).ToLowerInvariant()
                           where ext == ".appxbundle" || ext == ".eappxbundle" || ext == ".msixbundle" || ext == ".emsixbundle"
                           select file).ToList();

            var tempBundles = new List<AppxBundleFile>();
            try
            {
                foreach (var bundle in bundles)
                {
                    tempBundles.Add(new AppxBundleFile(bundle, logger, makeappxPath));
                }

                // See if there's any files in the expanded zip that we need to sign
                var allFiles = tempBundles.SelectMany(tz => tz.FilteredFilesInDirectory).ToList();
                if (allFiles.Count > 0)
                {
                    // Send the files from the archives through the aggregator to sign
                    await Submit(hashMode, name, description, descriptionUrl, allFiles, filter);

                    // After signing the contents, save the zip
                    tempBundles.ForEach(tz => tz.Save());
                }
            }
            finally
            {
                tempBundles.ForEach(tz => tz.Dispose());
                tempBundles.Clear();
            }

            // split by code sign service and fallback to default

            var grouped = (from kvp in codeSignServices
                           join file in files on kvp.Key equals Path.GetExtension(file).ToLowerInvariant()
                           group file by kvp.Value into g
                           select g).ToList();

            // get all files and exclude existing; 

            // This is to catch PE files that don't have the correct extension set
            var defaultFiles = files.Except(grouped.SelectMany(g => g))
                                    .Where(IsPeFile)
                                    .Select(f => new { defaultCodeSignService, f })
                                    .GroupBy(a => a.defaultCodeSignService, k => k.f)
                                    .SingleOrDefault(); // one group here

            if (defaultFiles != null)
            {
                grouped.Add(defaultFiles);
            }

            await Task.WhenAll(grouped.Select(g => g.Key.Submit(hashMode, name, description, descriptionUrl, g.ToList(), filter)));
        }

        static bool IsPeFile(string file)
        {
            using (var str = File.OpenRead(file))
            {
                var buffer = new byte[2];
                if (str.CanRead)
                {
                    var read = str.Read(buffer, 0, 2);
                    if (read == 2)
                    {
                        // Look for the magic MZ header 
                        return (buffer[0] == 0x4d && buffer[1] == 0x5a);
                    }
                }
            }

            return false;
        }
    }
}
