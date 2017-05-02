using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignService.SigningTools;
using SignService.Utils;

namespace SignService
{
    public interface ICodeSignService
    {
        Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files);

        IReadOnlyCollection<string> SupportedFileExtensions { get; }

        bool IsDefault { get; }
    }

    class SigntoolCodeSignService : ICodeSignService
    {
        readonly string timeStampUrl;
        readonly string thumbprint;
        readonly ILogger<SigntoolCodeSignService> logger;
        readonly IAppxFileFactory appxFileFactory;

        readonly string signtoolPath;

        // Four things at once as we're hitting the sign server
        readonly ParallelOptions options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4
        };


        public SigntoolCodeSignService(IOptionsSnapshot<Settings> settings, ILogger<SigntoolCodeSignService> logger, IAppxFileFactory appxFileFactory)
        {
            timeStampUrl = settings.Value.CertificateInfo.TimestampUrl;
            thumbprint = settings.Value.CertificateInfo.Thumbprint;
            this.logger = logger;
            this.appxFileFactory = appxFileFactory;
            signtoolPath = Path.Combine(settings.Value.WinSdkBinDirectory, "signtool.exe");
        }

        public Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files)
        {
            // Explicitly put this on a thread because Parallel.ForEach blocks
            if (hashMode == HashMode.Sha1)
                throw new ArgumentOutOfRangeException(nameof(hashMode), "Only Sha56 or Dual is supported");
            
            return Task.Run(() => SubmitInternal(hashMode, name, description, descriptionUrl, files));
        }

        void SubmitInternal(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files)
        {
            logger.LogInformation("Signing SignTool job {0} with {1} files", name, files.Count());

            var descArgsList = new List<string>();
            if (!string.IsNullOrWhiteSpace(description))
            {
                if (description.Contains("\""))
                    throw new ArgumentException(nameof(description));

                descArgsList.Add($@"/d ""{description}""");
            }
            if (!string.IsNullOrWhiteSpace(descriptionUrl))
            {
                if (descriptionUrl.Contains("\""))
                    throw new ArgumentException(nameof(descriptionUrl));

                descArgsList.Add($@"/du ""{descriptionUrl}""");
            }

            var descArgs = string.Join(" ", descArgsList);


            Parallel.ForEach(files, options, (file, state) =>
            {

                // check to see if it's an appx and strip it first
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (".appx".Equals(ext, StringComparison.OrdinalIgnoreCase))
                {
                    StripAppx(file);
                }

                string args;
                if (hashMode == HashMode.Dual)
                {
                    // Sign it with sha1

                    args = $@"sign /t {timeStampUrl} {descArgs} /sha1 {thumbprint} ""{file}""";

                    if (!Sign(args))
                    {
                        throw new Exception($"Could not sign {file}");
                    }
                   
                }

                var appendParam = hashMode == HashMode.Dual ? "/as" : string.Empty;

                args = $@"sign /tr {timeStampUrl} {appendParam} /fd sha256 /td sha256 {descArgs} /sha1 {thumbprint} ""{file}""";
                // Append a sha256 signature
                if (!Sign(args))
                {
                    throw new Exception($"Could not append sign {file}");
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

        void StripAppx(string appxFile)
        {
            // This will extract and resave the appx, stripping the signature
            // and fixing the publisher
            using (var appx = appxFileFactory.Create(appxFile))
            {
                appx.Save();
            }
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
                logger.LogInformation(@"""{0}"" {1}", signtool.StartInfo.FileName, signtool.StartInfo.Arguments);
                signtool.Start();
                if (!signtool.WaitForExit(30*1000))
                {
                    logger.LogError("Error: Signtool took too long to respond {0}", signtool.ExitCode);
                    try
                    {
                        signtool.Kill();
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("SignTool timed out and could not be killed", ex);
                    }

                    logger.LogError("Error: Signtool took too long to respond {0}", signtool.ExitCode);
                    throw new Exception($"Sign tool took too long to respond with {signtool.StartInfo.Arguments}");
                }

                if (signtool.ExitCode == 0)
                {
                    return true;
                }

                logger.LogError("Error: Signtool returned {0}", signtool.ExitCode);

                return false;
            }

        }

        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new List<string>()
        {
            ".msi",
            ".msp",
            ".msm",
            ".cab",
            ".dll",
            ".exe",
            ".sys",
            ".vxd",
            ".winmd",
            ".appx"
        };

        public bool IsDefault => true;
    }
}
