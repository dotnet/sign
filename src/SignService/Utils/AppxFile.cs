using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SignService.Utils
{
    public interface IAppxFileFactory
    {
        AppxFile Create(string inputFileName);
    }

    public class AppxFileFactory : IAppxFileFactory
    {
        readonly ILogger<AppxFileFactory> logger;
        readonly IOptionsSnapshot<Settings> settings;

        readonly string publisher;
        readonly string makeappxPath;

        public AppxFileFactory(ILogger<AppxFileFactory> logger, IOptionsSnapshot<Settings> settings)
        {
            this.logger = logger;
            this.settings = settings;
            makeappxPath = Path.Combine(settings.Value.WinSdkBinDirectory, "makeappx.exe");
            var thumbprint = settings.Value.CertificateInfo.Thumbprint;

            var cert = FindCertificate(thumbprint, StoreLocation.CurrentUser) ?? FindCertificate(thumbprint, StoreLocation.LocalMachine);

            publisher = cert.SubjectName.Name;
        }

        public AppxFile Create(string inputFileName)
        {
            return new AppxFile(inputFileName, publisher, logger, makeappxPath);
        }

        static X509Certificate2 FindCertificate(string thumbprint, StoreLocation location)
        {
            using (var store = new X509Store(StoreName.My, location))
            {
                store.Open(OpenFlags.ReadOnly);
                var certs = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, true);
                if (certs.Count == 0)
                    return null;

                return certs[0];
            }
        }
    }

    // Unpacking and repacking an appx will strip it of its signature
    // We can also update the publisher of the appxmanifest

    public class AppxFile : IDisposable
    {
        readonly string inputFileName;
        readonly string publisher;
        readonly ILogger logger;
        readonly string makeAppxPath;
        readonly string dataDirectory;

        public AppxFile(string inputFileName, string publisher, ILogger logger, string makeAppxPath)
        {
            this.inputFileName = inputFileName;
            this.publisher = publisher;
            this.logger = logger;
            this.makeAppxPath = makeAppxPath;

            dataDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dataDirectory);

            Unpack();
            UpdateManifestPublisher();
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
                    RedirectStandardError = false,
                    RedirectStandardOutput = false,
                    Arguments = args
                }
            })
            {
                logger.LogInformation($"Running Makeappx with parameters: '{args}'");
                makeappx.Start();
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


        public void Dispose()
        {
            Directory.Delete(dataDirectory, true);
        }
    }
}
