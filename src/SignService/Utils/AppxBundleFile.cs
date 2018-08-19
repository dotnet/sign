using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SignService.Utils;

namespace SignService
{
    public class AppxBundleFile : IDisposable
    {
        readonly string inputFileName;
        readonly ILogger logger;
        readonly string makeAppxPath;
        readonly string dataDirectory;
        readonly string bundleVersion;

        public AppxBundleFile(string inputFileName, ILogger logger, string makeAppxPath)
        {
            this.inputFileName = inputFileName;
            this.logger = logger;
            this.makeAppxPath = makeAppxPath;
            dataDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dataDirectory);

            logger.LogInformation($"Extracting bundle file {inputFileName}");
            Unbundle();

            bundleVersion = GetBundleVersion();

            var filesInDir = Directory.EnumerateFiles(dataDirectory, "*.appx", SearchOption.AllDirectories)
                                      .Concat(Directory.EnumerateFiles(dataDirectory, "*.msix", SearchOption.AllDirectories));

            FilteredFilesInDirectory = filesInDir.ToList();
        }

        public void Save()
        {
            Bundle();
        }

        void Unbundle()
        {
            var args = $@"unbundle /p {inputFileName} /d ""{dataDirectory}"" /o";
            RunTool(args);
        }

        string GetBundleVersion()
        {
            var fileName = Path.Combine(dataDirectory, "AppxMetadata", "AppxBundleManifest.xml");
            using (var fs = File.OpenRead(fileName))
            {
                var manifest = XDocument.Load(fs, LoadOptions.PreserveWhitespace);
                XNamespace ns = "http://schemas.microsoft.com/appx/2013/bundle";

                return manifest.Root?.Element(ns + "Identity")?.Attribute("Version")?.Value;
            }
        }

        void RunTool(string args)
        {
            using (var makeappx = new Process
            {
                StartInfo =
                {
                    FileName = makeAppxPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    Arguments = args
                }
            })
            {
                logger.LogInformation($"Running Makeappx with parameters: '{args}'");
                makeappx.Start();
                var output = makeappx.StandardOutput.ReadToEnd();
                var error = makeappx.StandardError.ReadToEnd();
                logger.LogInformation("MakeAppx Out {MakeAppxOutput}", output);

                if (!string.IsNullOrWhiteSpace(error))
                {
                    logger.LogInformation("MakeAppx Err {MakeAppxError}", error);
                }

                if (!makeappx.WaitForExit(30 * 1000))
                {
                    logger.LogError("Error: Makeappx took too long to respond {0}", makeappx.ExitCode);

                    try
                    {
                        makeappx.Kill();
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Makeappx timed out and could not be killed", ex);
                    }

                    logger.LogError("Error: Makeappx took too long to respond {0}", makeappx.ExitCode);
                    throw new Exception($"Makeappx took too long to respond with {makeappx.StartInfo.Arguments}");
                }
            }
        }

        void Bundle()
        {
            var args = $@"bundle /d ""{dataDirectory}"" /p {inputFileName} /bv {bundleVersion} /o ";
            RunTool(args);
        }

        public IList<string> FilteredFilesInDirectory { get; }

        public void Dispose()
        {
            DirectoryUtility.SafeDelete(dataDirectory);
        }

    }
}
