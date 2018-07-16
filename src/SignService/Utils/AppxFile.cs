using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Wyam.Core.IO.Globbing;

namespace SignService.Utils
{

    // Unpacking and repacking an appx will strip it of its signature
    // We can also update the publisher of the appxmanifest

    public class AppxFile : IDisposable
    {
        readonly string inputFileName;
        readonly string publisher;
        readonly ILogger logger;
        readonly string makeAppxPath;
        readonly string dataDirectory;

        public AppxFile(string inputFileName, string publisher, ILogger logger, string makeAppxPath, string filter)
        {
            this.inputFileName = inputFileName;
            this.publisher = publisher;
            this.logger = logger;
            this.makeAppxPath = makeAppxPath;

            dataDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dataDirectory);

            Unpack();
            UpdateManifestPublisher();


            var filesInDir = Directory.EnumerateFiles(dataDirectory, "*.*", SearchOption.AllDirectories);
            FilesInDirectory = filesInDir.ToList();

            // don't allow parent directory traversal
            filter = filter.Replace(@"..\", "").Replace("../", "");

            var globs = filter.Split('\n').Where(s => (!string.IsNullOrWhiteSpace(s)))
                              .Where(s => (!s.StartsWith("!")))
                              .ToList();

            var antiglobs = filter.Split('\n').Where(s => (!string.IsNullOrWhiteSpace(s)))
                                  .Where(s => (s.StartsWith("!")))
                                  .Select(s => s.Substring(1))
                                  .ToList();

            if (globs.Count > 0)
            {
                var files = Globber.GetFiles(new DirectoryInfo(dataDirectory), globs);
                FilteredFilesInDirectory = files.Select(f => f.FullName).ToList();
            }

            // If no filtered, default to all
            if (FilteredFilesInDirectory == null)
            {
                FilteredFilesInDirectory = FilesInDirectory.ToList();
            }

            if (antiglobs.Count > 0)
            {
                var antifiles = Globber.GetFiles(new DirectoryInfo(dataDirectory), antiglobs)
                                       .Select(f => f.FullName)
                                       .ToList();

                FilteredFilesInDirectory = FilteredFilesInDirectory.Except(antifiles).ToList();
            }

            FilesExceptFiltered = FilesInDirectory.Except(FilteredFilesInDirectory).ToList();
        }

        public void Save()
        {
            Pack();
        }

        void UpdateManifestPublisher()
        {
            var fileName = Path.Combine(dataDirectory, "AppxManifest.xml");
            XDocument manifest;
            using (var fs = File.OpenRead(fileName))
            {
                manifest = XDocument.Load(fs, LoadOptions.PreserveWhitespace);
                XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

                var idElement = manifest.Root?.Element(ns + "Identity");
                idElement?.SetAttributeValue("Publisher", publisher);
            }

            using (var fs = File.Create(fileName))
            {
                manifest.Save(fs);
            }
        }
        void Unpack()
        {
            var args = $@"unpack /p {inputFileName} /d ""{dataDirectory}"" /l /o";
            RunTool(args);
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

        void Pack()
        {
            var args = $@"pack /d ""{dataDirectory}"" /p {inputFileName} /o /l";
            RunTool(args);
        }

        public IList<string> FilteredFilesInDirectory { get; }
        public IList<string> FilesInDirectory { get; }
        public IList<string> FilesExceptFiltered { get; }

        public void Dispose()
        {
            DirectoryUtility.SafeDelete(dataDirectory);
        }
    }
}
