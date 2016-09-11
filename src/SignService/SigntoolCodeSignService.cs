using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SignService
{
    public interface ICodeSignService
    {
        void Submit(string name, string description, string descriptionUrl, IList<string> files);
    }

    class SigntoolCodeSignService : ICodeSignService
    {
        readonly string timeStampUrl;
        readonly string thumbprint;
        readonly ILogger<SigntoolCodeSignService> logger;

        readonly string signtoolPath;

        // Four things at once as we're hitting the sign server
        readonly ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = 4 };


        public SigntoolCodeSignService(string timeStampUrl, string thumbprint, string contentPath, ILogger<SigntoolCodeSignService> logger)
        {
            this.timeStampUrl = timeStampUrl;
            this.thumbprint = thumbprint;
            this.logger = logger;
            signtoolPath = Path.Combine(contentPath, "tools\\signtool.exe");
        }

        public void Submit(string name, string description, string descriptionUrl, IList<string> files)
        {
            logger.LogInformation("Signing job {0} with {1} files", name, files.Count());

            
            Parallel.ForEach(files, options, (file, state) =>
            {
                // Sign it with sha1
                var signtool = new Process
                {
                    StartInfo =
                    {
                        FileName = signtoolPath,
                        UseShellExecute = false,
                        RedirectStandardError = false,
                        RedirectStandardOutput = false,
                        Arguments = $@"sign /t {timeStampUrl} /d ""{description}"" /du {descriptionUrl} /sha1 {thumbprint} ""{file}"""
                    }
                };
                logger.LogInformation(@"""{0}"" {1}", signtool.StartInfo.FileName, signtool.StartInfo.Arguments);
                signtool.Start();
                signtool.WaitForExit();
                if (signtool.ExitCode != 0)
                {
                    logger.LogError("Error: Signtool returned {0}", signtool.ExitCode);
                    throw new Exception($"Sign tool returned error with {signtool.StartInfo.Arguments}");
                }
                signtool.Dispose();

                // Append a sha256 signature
                signtool = new Process
                {
                    StartInfo =
                    {
                        FileName = signtoolPath,
                        UseShellExecute = false,
                        RedirectStandardError = false,
                        RedirectStandardOutput = false,
                        Arguments = $@"sign /tr {timeStampUrl} /as /fd sha256 /td sha256 /d ""{description}"" /du {descriptionUrl} /sha1 {thumbprint} ""{file}"""
                    }
                };
                logger.LogInformation(@"""{0}"" {1}", signtool.StartInfo.FileName, signtool.StartInfo.Arguments);
                signtool.Start();
                signtool.WaitForExit();
                if (signtool.ExitCode != 0)
                {
                    logger.LogError("Error: Signtool returned {0}", signtool.ExitCode);
                    throw new Exception($"Sign tool returned error with {signtool.StartInfo.Arguments}");
                }
                signtool.Dispose();
            });
        }
    }
}
