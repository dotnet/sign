using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignService.Utils;

namespace SignService.SigningTools
{
    public class MageSignService : ICodeSignService
    {
        readonly AadOptions aadOptions;
        readonly CertificateInfo certificateInfo;
        readonly ILogger<MageSignService> logger;
        readonly string signtoolPath;
        readonly string timeStampUrl;
        readonly string thumbprint;
        readonly Lazy<ISigningToolAggregate> signToolAggregate;
        readonly ParallelOptions options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4
        };

        public MageSignService(IOptionsSnapshot<Settings> settings, IOptionsSnapshot<AadOptions> aadOptions, IHostingEnvironment hostingEnvironment, IServiceProvider serviceProvider, ILogger<MageSignService> logger)
        {
            timeStampUrl = settings.Value.CertificateInfo.TimestampUrl;
            thumbprint = settings.Value.CertificateInfo.Thumbprint;
            this.aadOptions = aadOptions.Value;
            certificateInfo = settings.Value.CertificateInfo;
            this.logger = logger;
            signtoolPath = Path.Combine(hostingEnvironment.ContentRootPath, "tools\\SDK\\mage.exe");
            // Need to delay this as it'd create a dependency loop if directly in the ctor
            signToolAggregate = new Lazy<ISigningToolAggregate>(() => serviceProvider.GetService<ISigningToolAggregate>());
        }
        public async Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files, string filter)
        {
            // Explicitly put this on a thread because Parallel.ForEach blocks
            await Task.Run(() => SubmitInternal(hashMode, name, description, descriptionUrl, files, filter));
        }

        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new List<string>
        {
            ".clickonce"
        };
        public bool IsDefault { get; }

        void SubmitInternal(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files, string filter)
        {
            logger.LogInformation("Signing Mage job {0} with {1} files", name, files.Count());

            // If KeyVault is enabled, use that

            // Dual isn't supported, use sha256
            var alg = hashMode == HashMode.Sha1 ? "sha1RSA" : "sha256RSA";
            string args = null;

            if (!certificateInfo.UseKeyVault)
            {
                args = $@"-ch {thumbprint} --ti {timeStampUrl} -a {alg} -n ""{name}"" ";
            }
            else
            {
                args = $@"sign --timestamp {timeStampUrl} -ta {alg} -fd {alg} -kvu {certificateInfo.KeyVaultUrl} -kvc {certificateInfo.KeyVaultCertificateName} -kvi {aadOptions.ClientId} -kvs {aadOptions.ClientSecret}";
            }
            
            
            // This outer loop is for a .clickonce file            
            Parallel.ForEach(files, options, (file, state) =>
            {

                // We need to be explicit about the order these files are signed in. The data files must be signed first
                // Then the .manifest file
                // Then the nested clickonce/vsto file
                // finally the top-level clickonce/vsto file

                using (var zip = new TemporaryZipFile(file, filter, logger))
                {
                    // Look for the data files first - these are .deploy files
                    // we need to rename them, sign, then restore the name

                    var deployFilesToSign = zip.FilteredFilesInDirectory.Where(f => ".deploy".Equals(Path.GetExtension(f), StringComparison.OrdinalIgnoreCase))                                                                                                   
                                                                .ToList();

                    var contentFiles = new List<string>();
                    foreach(var dfile in deployFilesToSign)
                    {
                        // Rename to file without extension
                        var dest = dfile.Replace(".deploy", "");
                        File.Move(dfile, dest);
                        contentFiles.Add(dest);
                    }

                    // Safe to call Wait here because we're in a Parallel.ForEach()
                    // sign the inner files
                    signToolAggregate.Value.Submit(hashMode, name, description, descriptionUrl, contentFiles, filter).Wait();

                    // rename the rest of the deploy files since signing the manifest will need them
                    var deployFiles = zip.FilesExceptFiltered.Where(f => ".deploy".Equals(Path.GetExtension(f), StringComparison.OrdinalIgnoreCase))
                                                             .ToList();

                    foreach (var dfile in deployFiles)
                    {
                        // Rename to file without extension
                        var dest = dfile.Replace(".deploy", "");
                        File.Move(dfile, dest);
                        contentFiles.Add(dest);
                    }

                    // at this point contentFiles has all deploy files renamed

                    // Inner files are now signed
                    // now look for the manifest file and sign that

                    var manifestFile = zip.FilteredFilesInDirectory.Single(f => ".manifest".Equals(Path.GetExtension(f), StringComparison.OrdinalIgnoreCase));

                    var fileArgs = $@"-update ""{manifestFile}"" {args}";

                    if (!Sign(fileArgs))
                    {
                        throw new Exception($"Could not sign {manifestFile}");
                    }

                    // Now sign the inner vsto/clickonce file
                    // Order by desending length to put the inner one first
                    var filesToSign = zip.FilteredFilesInDirectory.Where(f => ".vsto".Equals(Path.GetExtension(f), StringComparison.OrdinalIgnoreCase) || ".application".Equals(Path.GetExtension(f), StringComparison.OrdinalIgnoreCase))
                                                                  .Select(f => new { file = f, f.Length })
                                                                  .OrderByDescending(f => f.Length)
                                                                  .Select(f => f.file)
                                                                  .ToList();

                    foreach(var f in filesToSign)
                    {
                        fileArgs = $@"-update ""{f}"" {args} -appm ""{manifestFile}""";
                        if (!Sign(fileArgs))
                        {
                            throw new Exception($"Could not sign {f}");
                        }
                    }

                    // restore the deploy files
                    foreach(var dfile in contentFiles)
                    {
                        File.Move(dfile, $"{dfile}.deploy");
                    }

                    zip.Save();
                }                    

                
            });
        }

        // Inspired from https://github.com/squaredup/bettersigntool/blob/master/bettersigntool/bettersigntool/SignCommand.cs

        bool Sign(string args)
        {
            var retry = TimeSpan.FromSeconds(5);
            var attempt = 1;
            do
            {
                if (attempt > 1)
                {
                    logger.LogInformation($"Performing attempt #{attempt} of 3 attempts after {retry.TotalSeconds}s");
                    Thread.Sleep(retry);
                }

                if (RunSignTool(args))
                {
                    logger.LogInformation($"Signed {args}");
                    return true;
                }

                attempt++;

                retry = TimeSpan.FromSeconds(Math.Pow(retry.TotalSeconds, 1.5));

            } while (attempt <= 3);

            logger.LogError($"Failed to sign {args}. Attempts exceeded");

            return false;
        }

        bool RunSignTool(string args)
        {
            // Append a sha256 signature
            using (var signtool = new Process
            {
                StartInfo =
                {
                    FileName = signtoolPath,
                    UseShellExecute = false,
                    RedirectStandardError = false,
                    RedirectStandardOutput = false,
                    Arguments = args
                }
            })
            {
                logger.LogInformation("Signing {fileName}", signtool.StartInfo.FileName);
                signtool.Start();
                if (!signtool.WaitForExit(30 * 1000))
                {
                    logger.LogError("Error: Mage took too long to respond {exitCode}", signtool.ExitCode);
                    try
                    {
                        signtool.Kill();
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Mage timed out and could not be killed", ex);
                    }

                    logger.LogError("Error: Mage took too long to respond {exitCode}", signtool.ExitCode);
                    throw new Exception($"Mage took too long to respond");
                }

                if (signtool.ExitCode == 0)
                {
                    return true;
                }

                logger.LogError("Error: Signtool returned {exitCode}", signtool.ExitCode);

                return false;
            }

        }
    }
}
