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
    public interface ICodeSignService
    {
        Task Submit(string name, string description, string descriptionUrl, IList<string> files);

        IReadOnlyCollection<string> SupportedFileExtensions { get; }

        bool IsDefault { get; }
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

        public Task Submit(string name, string description, string descriptionUrl, IList<string> files)
        {
            // Explicitly put this on a thread because Parallel.ForEach blocks
            return Task.Run(() => SubmitInternal(name, description, descriptionUrl, files));
        }

        void SubmitInternal(string name, string description, string descriptionUrl, IList<string> files)
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
                // Sign it with sha1
                var signtool = new Process
                {
                    StartInfo =
                    {
                        FileName = signtoolPath,
                        UseShellExecute = false,
                        RedirectStandardError = false,
                        RedirectStandardOutput = false,
                        Arguments = $@"sign /t {timeStampUrl} {descArgs} /sha1 {thumbprint} ""{file}"""
                    }
                };
                logger.LogInformation(@"""{0}"" {1}", signtool.StartInfo.FileName, signtool.StartInfo.Arguments);
                signtool.Start();
                if (!signtool.WaitForExit(30 * 1000))
                {
                    signtool.Kill();
                    logger.LogError("Error: Signtool took too long to respond {0}", signtool.ExitCode);
                    throw new Exception($"Sign tool took too long to respond with {signtool.StartInfo.Arguments}");
                }
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
                        Arguments = $@"sign /tr {timeStampUrl} /as /fd sha256 /td sha256 {descArgs} /sha1 {thumbprint} ""{file}"""
                    }
                };
                logger.LogInformation(@"""{0}"" {1}", signtool.StartInfo.FileName, signtool.StartInfo.Arguments);
                signtool.Start();
                if (!signtool.WaitForExit(30 * 1000))
                {
                    signtool.Kill();
                    logger.LogError("Error: Signtool took too long to respond {0}", signtool.ExitCode);
                    throw new Exception($"Sign tool took too long to respond with {signtool.StartInfo.Arguments}");
                }
                if (signtool.ExitCode != 0)
                {
                    logger.LogError("Error: Signtool returned {0}", signtool.ExitCode);
                    throw new Exception($"Sign tool returned error with {signtool.StartInfo.Arguments}");
                }
                signtool.Dispose();
            });
        }

        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new List<string>();
        public bool IsDefault => true;
    }
}
