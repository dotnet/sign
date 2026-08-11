// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed class ClickOnceManifestReaderTests
    {
        private const string TargetFrameworkVersion = "v4.5";

        [Fact]
        public void TryReadApplicationManifest_WhenManifestIsApplication_ReturnsTypedWritableAdapter()
        {
            ApplicationManifest expectedManifest = CreateApplicationManifest();
            using MemoryStream stream = WriteManifest(expectedManifest);
            ClickOnceManifestReader reader = new();

            bool result = reader.TryReadApplicationManifest(
                stream,
                preserveStream: true,
                out IApplicationManifest? manifest);

            Assert.True(result);
            Assert.NotNull(manifest);
            Assert.Equal("TestApplication", manifest.AssemblyIdentity.Name);
            Assert.NotNull(manifest.OutputMessages);

            manifest.ReadOnly = true;

            Assert.True(manifest.ReadOnly);

            manifest.ReadOnly = false;

            Assert.False(manifest.ReadOnly);
        }

        [Fact]
        public void TryReadDeployManifest_WhenManifestIsDeployment_ReturnsTypedWritableAdapter()
        {
            DeployManifest expectedManifest = CreateDeployManifest();
            using MemoryStream stream = WriteManifest(expectedManifest);
            ClickOnceManifestReader reader = new();

            bool result = reader.TryReadDeployManifest(
                stream,
                preserveStream: true,
                out IDeployManifest? manifest);

            Assert.True(result);
            Assert.NotNull(manifest);
            Assert.Equal("TestDeployment", manifest.AssemblyIdentity.Name);
            Assert.True(manifest.MapFileExtensions);

            manifest.ReadOnly = true;

            Assert.True(manifest.ReadOnly);

            manifest.ReadOnly = false;

            Assert.False(manifest.ReadOnly);
        }

        [Fact]
        public void TryReadApplicationManifest_WhenManifestIsDeployment_ReturnsFalse()
        {
            using MemoryStream stream = WriteManifest(CreateDeployManifest());
            ClickOnceManifestReader reader = new();

            bool result = reader.TryReadApplicationManifest(
                stream,
                preserveStream: true,
                out IApplicationManifest? manifest);

            Assert.False(result);
            Assert.Null(manifest);
            Assert.Equal(0, stream.Position);
            Assert.True(
                reader.TryReadDeployManifest(
                    stream,
                    preserveStream: true,
                    out IDeployManifest? deployManifest));
            Assert.NotNull(deployManifest);
        }

        [Fact]
        public void TryReadDeployManifest_WhenManifestIsApplication_ReturnsFalse()
        {
            using MemoryStream stream = WriteManifest(CreateApplicationManifest());
            ClickOnceManifestReader reader = new();

            bool result = reader.TryReadDeployManifest(
                stream,
                preserveStream: true,
                out IDeployManifest? manifest);

            Assert.False(result);
            Assert.Null(manifest);
            Assert.Equal(0, stream.Position);
            Assert.True(
                reader.TryReadApplicationManifest(
                    stream,
                    preserveStream: true,
                    out IApplicationManifest? applicationManifest));
            Assert.NotNull(applicationManifest);
        }

        [Fact]
        public void TryReadApplicationManifest_WhenManifestIsSideBySide_ReturnsFalse()
        {
            const string Xml = """
                <?xml version="1.0" encoding="utf-8"?>
                <assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
                  <assemblyIdentity name="SideBySide" version="1.0.0.0" processorArchitecture="msil" type="win32" />
                </assembly>
                """;

            using MemoryStream stream = new(Encoding.UTF8.GetBytes(Xml));
            ClickOnceManifestReader reader = new();

            bool result = reader.TryReadApplicationManifest(
                stream,
                preserveStream: true,
                out IApplicationManifest? manifest);

            Assert.False(result);
            Assert.Null(manifest);
        }

        [Fact]
        public void ApplicationManifestAdapter_UpdateFileInfo_UsesSha256()
        {
            string temporaryFilePath = Path.GetTempFileName();

            try
            {
                File.WriteAllText(temporaryFilePath, "payload");
                ApplicationManifest applicationManifest = CreateApplicationManifest();
                FileReference reference = new(temporaryFilePath);
                applicationManifest.FileReferences.Add(reference);
                ApplicationManifestAdapter adapter = new(applicationManifest);

                adapter.ResolveFiles(
                    new[] { new FileInfo(temporaryFilePath).Directory! });
                adapter.UpdateFileInfo();

                Assert.Equal(
                    SHA256.HashData(File.ReadAllBytes(temporaryFilePath)),
                    Convert.FromBase64String(reference.Hash));
                Assert.Equal(new FileInfo(temporaryFilePath).Length, reference.Size);
            }
            finally
            {
                File.Delete(temporaryFilePath);
            }
        }

        [Fact]
        public void ApplicationManifestAdapter_Properties_ForwardToManifest()
        {
            ApplicationManifest manifest = CreateApplicationManifest();
            AssemblyReference entryPoint = new();
            manifest.AssemblyReferences.Add(entryPoint);
            manifest.EntryPoint = entryPoint;
            ApplicationManifestAdapter adapter = new(manifest);

            Assert.Same(manifest.AssemblyIdentity, adapter.AssemblyIdentity);
            Assert.Same(manifest.AssemblyReferences, adapter.AssemblyReferences);
            Assert.Same(manifest.EntryPoint, adapter.EntryPoint);
            Assert.Same(manifest.FileReferences, adapter.FileReferences);
            Assert.Same(manifest.OutputMessages, adapter.OutputMessages);
        }

        [Fact]
        public void DeployManifestAdapter_Properties_ForwardToManifest()
        {
            DeployManifest manifest = CreateDeployManifest();
            AssemblyReference entryPoint = new();
            manifest.AssemblyReferences.Add(entryPoint);
            manifest.EntryPoint = entryPoint;
            DeployManifestAdapter adapter = new(manifest);

            Assert.Same(manifest.AssemblyIdentity, adapter.AssemblyIdentity);
            Assert.Same(manifest.AssemblyReferences, adapter.AssemblyReferences);
            Assert.Same(manifest.EntryPoint, adapter.EntryPoint);
            Assert.Same(manifest.OutputMessages, adapter.OutputMessages);
            Assert.Equal(manifest.MapFileExtensions, adapter.MapFileExtensions);
        }

        [Fact]
        public void ApplicationManifestAdapter_ResolveFiles_PreservesSearchOrder()
        {
            const string PayloadFileName = "payload.dll";

            using DirectoryServiceStub directoryService = new();
            DirectoryInfo firstDirectory = directoryService.CreateTemporaryDirectory();
            DirectoryInfo secondDirectory = directoryService.CreateTemporaryDirectory();
            string firstPayloadPath = Path.Combine(
                firstDirectory.FullName,
                PayloadFileName);
            string secondPayloadPath = Path.Combine(
                secondDirectory.FullName,
                PayloadFileName);
            File.WriteAllText(firstPayloadPath, contents: "first");
            File.WriteAllText(secondPayloadPath, contents: "second");
            ApplicationManifest manifest = CreateApplicationManifest();
            FileReference reference = new(PayloadFileName);
            manifest.FileReferences.Add(reference);
            ApplicationManifestAdapter adapter = new(manifest);

            adapter.ResolveFiles(
                new[] { firstDirectory, firstDirectory, secondDirectory });

            Assert.Equal(firstPayloadPath, reference.ResolvedPath);
        }

        [Fact]
        public void ApplicationManifestAdapter_ResolveFiles_AllowsNoSearchDirectories()
        {
            const string ExpectedMessageName =
                "GenerateManifest.ResolveFailedInReadWriteMode";

            ApplicationManifest manifest = CreateApplicationManifest();
            manifest.FileReferences.Add(
                new FileReference(path: "missing.dll"));
            ApplicationManifestAdapter adapter = new(manifest);

            adapter.ResolveFiles(Array.Empty<DirectoryInfo>());

            Assert.Equal(
                expected: 1,
                actual: adapter.OutputMessages.ErrorCount);
            OutputMessage message = adapter.OutputMessages[0];
            Assert.Equal(ExpectedMessageName, message.Name);
            Assert.Equal(OutputMessageType.Error, message.Type);
            Assert.False(string.IsNullOrWhiteSpace(message.Text));
        }

        [Fact]
        public void ApplicationManifestAdapter_ResolveFiles_RejectsNullArguments()
        {
            ApplicationManifestAdapter adapter = new(CreateApplicationManifest());

            ArgumentNullException nullCollectionException =
                Assert.Throws<ArgumentNullException>(
                () => adapter.ResolveFiles(searchDirectories: null!));
            ArgumentException nullElementException =
                Assert.Throws<ArgumentException>(
                () => adapter.ResolveFiles(new DirectoryInfo[] { null! }));

            Assert.Equal(
                "searchDirectories",
                nullCollectionException.ParamName);
            Assert.Equal(
                expected: "searchDirectories",
                actual: nullElementException.ParamName);
        }

        [Fact]
        public void DeployManifestAdapter_ResolveFiles_RejectsNullArguments()
        {
            DeployManifestAdapter adapter = new(CreateDeployManifest());

            ArgumentNullException nullCollectionException =
                Assert.Throws<ArgumentNullException>(
                    () => adapter.ResolveFiles(searchDirectories: null!));
            ArgumentException nullElementException =
                Assert.Throws<ArgumentException>(
                    () => adapter.ResolveFiles(
                        new DirectoryInfo[] { null! }));

            Assert.Equal(
                "searchDirectories",
                nullCollectionException.ParamName);
            Assert.Equal(
                expected: "searchDirectories",
                actual: nullElementException.ParamName);
        }

        [Fact]
        public void ApplicationManifestAdapter_Write_PreservesSha256DigestMethod()
        {
            using TemporaryFile payload = new();
            using TemporaryFile output = new();
            string payloadPath = payload.File.FullName;
            string manifestPath = output.File.FullName;

            File.WriteAllText(payloadPath, contents: "payload");
            ApplicationManifest manifest = CreateApplicationManifest();
            manifest.FileReferences.Add(new FileReference(payloadPath));
            ApplicationManifestAdapter adapter = new(manifest);

            adapter.ResolveFiles(new[] { payload.File.Directory! });
            adapter.UpdateFileInfo();
            adapter.Write(output.File);

            AssertSha256Digest(manifestPath, payloadPath);
        }

        [Fact]
        public void DeployManifestAdapter_Write_PreservesSha256DigestMethod()
        {
            using TemporaryFile applicationManifestFile = new();
            using TemporaryFile deployManifestFile = new();
            string applicationManifestPath = applicationManifestFile.File.FullName;
            string deployManifestPath = deployManifestFile.File.FullName;

            ApplicationManifest applicationManifest = CreateApplicationManifest();
            ManifestWriter.WriteManifest(
                applicationManifest,
                applicationManifestPath,
                TargetFrameworkVersion);

            DeployManifest deployManifest = CreateDeployManifest();
            AssemblyReference entryPoint = new(applicationManifestPath)
            {
                AssemblyIdentity = applicationManifest.AssemblyIdentity,
                ReferenceType = AssemblyReferenceType.ClickOnceManifest
            };
            deployManifest.AssemblyReferences.Add(entryPoint);
            deployManifest.EntryPoint = entryPoint;
            DeployManifestAdapter adapter = new(deployManifest);

            adapter.ResolveFiles(new[] { applicationManifestFile.File.Directory! });
            adapter.UpdateFileInfo();
            adapter.Write(deployManifestFile.File);

            AssertSha256Digest(deployManifestPath, applicationManifestPath);
        }

        [Fact]
        public void ApplicationManifestAdapter_Write_OverwritesAndRoundTripsManifest()
        {
            using TemporaryFile output = new();
            string manifestPath = output.File.FullName;
            File.WriteAllText(manifestPath, contents: "junk");

            ApplicationManifestAdapter adapter = new(CreateApplicationManifest());
            adapter.Write(output.File);

            using FileStream stream = File.OpenRead(manifestPath);
            ClickOnceManifestReader reader = new();
            Assert.True(
                reader.TryReadApplicationManifest(
                    stream,
                    preserveStream: true,
                    out IApplicationManifest? manifest));
            Assert.NotNull(manifest);
            Assert.Equal("TestApplication", manifest.AssemblyIdentity.Name);
        }

        [Fact]
        public void ApplicationManifestAdapter_Write_DoesNotCreateParentDirectory()
        {
            const string ManifestFileName = "app.manifest";
            const string MissingDirectoryName = "missing";

            using DirectoryServiceStub directoryService = new();
            DirectoryInfo rootDirectory =
                directoryService.CreateTemporaryDirectory();
            FileInfo output = new(
                Path.Combine(
                    rootDirectory.FullName,
                    MissingDirectoryName,
                    ManifestFileName));
            ApplicationManifestAdapter adapter = new(CreateApplicationManifest());

            Assert.Throws<DirectoryNotFoundException>(
                () => adapter.Write(output));
            Assert.False(Directory.Exists(output.DirectoryName));
        }

        [Fact]
        public void ApplicationManifestAdapter_Write_WhenFileIsNull_Throws()
        {
            ApplicationManifestAdapter adapter = new(CreateApplicationManifest());

            Assert.Throws<ArgumentNullException>(
                () => adapter.Write(file: null!));
        }

        [Fact]
        public void DeployManifestAdapter_Write_WhenFileIsNull_Throws()
        {
            DeployManifestAdapter adapter = new(CreateDeployManifest());

            Assert.Throws<ArgumentNullException>(
                () => adapter.Write(file: null!));
        }

        [Fact]
        public void ApplicationManifestAdapter_Constructor_WhenManifestIsNull_Throws()
        {
            ArgumentNullException exception =
                Assert.Throws<ArgumentNullException>(
                    () => new ApplicationManifestAdapter(manifest: null!));

            Assert.Equal(
                expected: "manifest",
                actual: exception.ParamName);
        }

        [Fact]
        public void DeployManifestAdapter_Constructor_WhenManifestIsNull_Throws()
        {
            ArgumentNullException exception =
                Assert.Throws<ArgumentNullException>(
                    () => new DeployManifestAdapter(manifest: null!));

            Assert.Equal(
                expected: "manifest",
                actual: exception.ParamName);
        }

        private static ApplicationManifest CreateApplicationManifest()
        {
            ApplicationManifest manifest = new();
            manifest.AssemblyIdentity.Name = "TestApplication";
            manifest.AssemblyIdentity.Version = "1.0.0.0";
            manifest.AssemblyIdentity.ProcessorArchitecture = "msil";

            return manifest;
        }

        private static DeployManifest CreateDeployManifest()
        {
            DeployManifest manifest = new()
            {
                MapFileExtensions = true
            };
            manifest.AssemblyIdentity.Name = "TestDeployment";
            manifest.AssemblyIdentity.Version = "1.0.0.0";
            manifest.AssemblyIdentity.ProcessorArchitecture = "msil";

            return manifest;
        }

        private static MemoryStream WriteManifest(Manifest manifest)
        {
            MemoryStream stream = new();
            ManifestWriter.WriteManifest(manifest, stream);
            stream.Position = 0;

            return stream;
        }

        private static void AssertSha256Digest(string manifestPath, string referencedFilePath)
        {
            const string Sha256Algorithm = "http://www.w3.org/2000/09/xmldsig#sha256";

            XNamespace dsig = "http://www.w3.org/2000/09/xmldsig#";
            XDocument document = XDocument.Load(manifestPath);
            XElement digestMethod = Assert.Single(document.Descendants(dsig + "DigestMethod"));
            XElement digestValue = Assert.Single(document.Descendants(dsig + "DigestValue"));

            Assert.Equal(
                Sha256Algorithm,
                digestMethod.Attribute(name: "Algorithm")?.Value);
            Assert.Equal(
                SHA256.HashData(File.ReadAllBytes(referencedFilePath)),
                Convert.FromBase64String(digestValue.Value));
        }
    }
}
