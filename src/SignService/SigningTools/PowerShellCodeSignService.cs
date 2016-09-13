using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SignService
{


    class PowerShellCodeSignService : ICodeSignService
    {
        readonly string timeStampUrl;
        readonly string thumbprint;
        readonly ILogger<PowerShellCodeSignService> logger;
        

        // Four things at once as we're hitting the sign server
        readonly ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = 4 };


        public PowerShellCodeSignService(string timeStampUrl, string thumbprint,ILogger<PowerShellCodeSignService> logger)
        {
            this.timeStampUrl = timeStampUrl;
            this.thumbprint = thumbprint;
            this.logger = logger;
        }

        public Task Submit(string name, string description, string descriptionUrl, IList<string> files)
        {
            // Explicitly put this on a thread because Parallel.ForEach blocks
            return Task.Run(() => SubmitInternal(name, description, descriptionUrl, files));
        }

        void SubmitInternal(string name, string description, string descriptionUrl, IList<string> files)
        {
            logger.LogInformation("Signing PowerShell job {0} with {1} files", name, files.Count());



            Parallel.ForEach(files, options, (file, state) =>
            {
                // Sign it 
                var signtool = new Process
                {
                    StartInfo =
                    {
                        FileName = "powershell.exe",
                        UseShellExecute = false,
                        RedirectStandardError = false,
                        RedirectStandardOutput = false,
                        Arguments = $@"-Command ""Set-AuthenticodeSignature {file} @(Get-ChildItem -recurse cert: | where {{$_.Thumbprint -eq '{thumbprint}'}})[0] -TimestampServer '{timeStampUrl}'"""
                    }
                };
                logger.LogInformation(@"""{0}"" {1}", signtool.StartInfo.FileName, signtool.StartInfo.Arguments);
                signtool.Start();
                if (!signtool.WaitForExit(30 * 1000))
                {
                    signtool.Kill();
                    logger.LogError("Error: Set-AuthenticodeSignature took too long to respond {0}", signtool.ExitCode);
                    throw new Exception($"Set-AuthenticodeSignature took too long to respond with {signtool.StartInfo.Arguments}");
                }
                if (signtool.ExitCode != 0)
                {
                    logger.LogError("Error: Set-AuthenticodeSignature returned {0}", signtool.ExitCode);
                    throw new Exception($"Set-AuthenticodeSignature returned error with {signtool.StartInfo.Arguments}");
                }
                signtool.Dispose();
               
            });
        }

        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new List<string>
        {
            ".ps1",
            ".psm1"
        };
        public bool IsDefault => false;
    }
}
