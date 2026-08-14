// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed class ClickOnceManifestReaderTests
    {
        private const string AssemblyV1Namespace =
            "urn:schemas-microsoft-com:asm.v1";
        private const string AssemblyV2Namespace =
            "urn:schemas-microsoft-com:asm.v2";
        private const string SignatureNamespace =
            "http://www.w3.org/2000/09/xmldsig#";
        private const string TargetFrameworkVersion = "v4.5";
        private const string VstaV3Namespace =
            "urn:schemas-microsoft-com:vsta.v3";
        private const string VstoV4Namespace =
            "urn:schemas-microsoft-com:vsto.v4";

        [Fact]
        public void TryReadApplicationManifest_WhenManifestIsApplication_ReturnsTypedWritableAdapter()
        {
            ApplicationManifest expectedManifest = CreateApplicationManifest();
            using MemoryStream stream = WriteManifest(expectedManifest);
            ClickOnceManifestReader reader = new();

            bool result = reader.TryReadApplicationManifest(
                stream,
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
                out IApplicationManifest? manifest);

            Assert.False(result);
            Assert.Null(manifest);
            Assert.Equal(0, stream.Position);
            Assert.True(
                reader.TryReadDeployManifest(
                    stream,
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
                out IDeployManifest? manifest);

            Assert.False(result);
            Assert.Null(manifest);
            Assert.Equal(0, stream.Position);
            Assert.True(
                reader.TryReadApplicationManifest(
                    stream,
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
                out IApplicationManifest? manifest);

            Assert.False(result);
            Assert.Null(manifest);
        }

        [Fact]
        public void TryReadApplicationManifest_WhenManifestIsVsto_PreservesVstoXml()
        {
            using DirectoryServiceStub directoryService = new();
            DirectoryInfo directory =
                directoryService.CreateTemporaryDirectory();
            string payloadPath =
                Path.Combine(directory.FullName, "payload.dll");
            File.WriteAllText(payloadPath, contents: "updated payload");
            using FileStream stream = File.OpenRead(GetVstoManifestPath());
            ClickOnceManifestReader reader = new();

            Assert.True(
                reader.TryReadApplicationManifest(
                    stream,
                    out IApplicationManifest? manifest));
            Assert.NotNull(manifest);

            XElement expectedVstoExtension = Assert.Single(
                XDocument.Load(GetVstoManifestPath())
                    .Descendants(XName.Get("addIn", VstaV3Namespace)));
            FileInfo output = new(
                Path.Combine(directory.FullName, "output.manifest"));

            manifest.ResolveFiles(new[] { directory });
            manifest.UpdateFileInfo();
            manifest.Write(output);

            XDocument document = XDocument.Load(output.FullName);
            XElement actualVstoExtension = Assert.Single(
                document.Descendants(XName.Get("addIn", VstaV3Namespace)));
            XElement appAddIn = Assert.Single(
                actualVstoExtension.Descendants(
                    XName.Get("appAddIn", VstoV4Namespace)));

            Assert.True(
                XNode.DeepEquals(
                    NormalizeElement(expectedVstoExtension),
                    NormalizeElement(actualVstoExtension)));
            Assert.Equal("VstoTestAddIn", appAddIn.Attribute("keyName")?.Value);
            Assert.Empty(
                document.Descendants(
                    XName.Get("publisherIdentity", AssemblyV2Namespace)));
            Assert.Empty(
                document.Descendants(
                    XName.Get("Signature", SignatureNamespace)));
            AssertSha256Digest(output.FullName, payloadPath);
        }

        [Fact]
        public void ApplicationManifestAdapter_WhenWrittenTwice_PreservesVstoXml()
        {
            XDocument original = XDocument.Load(GetVstoManifestPath());
            using FileStream stream = File.OpenRead(GetVstoManifestPath());
            ClickOnceManifestReader reader = new();
            Assert.True(
                reader.TryReadApplicationManifest(
                    stream,
                    out IApplicationManifest? manifest));
            Assert.NotNull(manifest);
            using TemporaryFile firstOutput = new();
            using TemporaryFile secondOutput = new();

            manifest.Write(firstOutput.File);
            manifest.Write(secondOutput.File);

            AssertVstoXmlPreserved(
                original,
                XDocument.Load(firstOutput.File.FullName));
            AssertVstoXmlPreserved(
                original,
                XDocument.Load(secondOutput.File.FullName));
        }

        [Fact]
        public void TryReadDeployManifest_PreservesUnknownXml()
        {
            const string Xml = """
                <?xml version="1.0" encoding="utf-8"?>
                <asmv1:assembly
                    manifestVersion="1.0"
                    xmlns:asmv1="urn:schemas-microsoft-com:asm.v1"
                    xmlns="urn:schemas-microsoft-com:asm.v2"
                    xmlns:test="urn:test">
                  <asmv1:assemblyIdentity
                      name="TestDeployment"
                      version="1.0.0.0"
                      processorArchitecture="msil"
                      type="win32" />
                  <description xmlns="urn:schemas-microsoft-com:asm.v1" />
                  <deployment install="false" mapFileExtensions="true" />
                  <test:extension test:attribute="preserved" />
                  <publisherIdentity
                      name="CN=Old Publisher"
                      issuerKeyHash="00112233445566778899aabbccddeeff00112233" />
                </asmv1:assembly>
                """;

            using MemoryStream stream = CreateStream(Xml);
            ClickOnceManifestReader reader = new();
            using TemporaryFile output = new();

            Assert.True(
                reader.TryReadDeployManifest(
                    stream,
                    out IDeployManifest? manifest));
            Assert.NotNull(manifest);

            manifest.Write(output.File);

            XNamespace testNamespace = "urn:test";
            XElement extension = Assert.Single(
                XDocument.Load(output.File.FullName)
                    .Descendants(testNamespace + "extension"));

            Assert.Equal(
                "preserved",
                extension.Attribute(testNamespace + "attribute")?.Value);
            Assert.Empty(
                XDocument.Load(output.File.FullName)
                    .Descendants(
                        XName.Get(
                            "publisherIdentity",
                            AssemblyV2Namespace)));
        }

        [Fact]
        public void TryReadApplicationManifest_WhenStreamPositionIsNotZero_Throws()
        {
            using MemoryStream stream =
                WriteManifest(CreateApplicationManifest());
            stream.Position = 1;
            ClickOnceManifestReader reader = new();

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => reader.TryReadApplicationManifest(stream, out _));

            Assert.Equal("stream", exception.ParamName);
        }

        [Fact]
        public void TryReadApplicationManifest_WhenStreamIsNotSeekable_Throws()
        {
            using MemoryStream innerStream =
                WriteManifest(CreateApplicationManifest());
            using NonSeekableReadStream stream = new(innerStream);
            ClickOnceManifestReader reader = new();

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => reader.TryReadApplicationManifest(stream, out _));

            Assert.Equal("stream", exception.ParamName);
        }

        [Fact]
        public void TryReadApplicationManifest_WhenStreamIsNotReadable_Throws()
        {
            using MemoryStream stream = new(
                buffer: new byte[1],
                index: 0,
                count: 1,
                writable: true,
                publiclyVisible: true);
            stream.Close();
            ClickOnceManifestReader reader = new();

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => reader.TryReadApplicationManifest(stream, out _));

            Assert.Equal("stream", exception.ParamName);
        }

        [Fact]
        public void TryReadApplicationManifest_WhenStreamIsNull_Throws()
        {
            ClickOnceManifestReader reader = new();

            ArgumentNullException exception =
                Assert.Throws<ArgumentNullException>(
                    () => reader.TryReadApplicationManifest(
                        stream: null!,
                        out _));

            Assert.Equal("stream", exception.ParamName);
        }

        [Theory]
        [InlineData(
            "<!DOCTYPE assembly [<!ENTITY identityName \"InternalName\">]>",
            "&identityName;")]
        [InlineData(
            "<!DOCTYPE assembly SYSTEM \"file:///missing-vsto-test.dtd\">",
            "ExternalName")]
        public void TryReadApplicationManifest_DtdBehaviorMatchesManifestUtilities(
            string documentType,
            string identityName)
        {
            string xml = CreateApplicationManifestXml(
                documentType,
                identityName);
            Manifest? expectedManifest = null;
            Exception? expectedException;

            using (MemoryStream stream = CreateStream(xml))
            {
                expectedException = Record.Exception(
                    () => expectedManifest = ManifestReader.ReadManifest(
                        stream,
                        preserveStream: true));
            }

            IApplicationManifest? actualManifest = null;
            Exception? actualException;

            using (MemoryStream stream = CreateStream(xml))
            {
                ClickOnceManifestReader reader = new();
                actualException = Record.Exception(
                    () => reader.TryReadApplicationManifest(
                        stream,
                        out actualManifest));
            }

            Assert.Equal(
                expectedException?.GetType(),
                actualException?.GetType());

            if (expectedException is null)
            {
                Assert.NotNull(expectedManifest);
                Assert.NotNull(actualManifest);
                Assert.Equal(
                    expectedManifest.AssemblyIdentity.Name,
                    actualManifest.AssemblyIdentity.Name);
                using TemporaryFile output = new();

                actualManifest.Write(output.File);

                using FileStream outputStream =
                    File.OpenRead(output.File.FullName);
                Assert.True(
                    new ClickOnceManifestReader()
                        .TryReadApplicationManifest(outputStream, out _));
            }
        }

        [Fact]
        public void TryReadApplicationManifest_WhenXmlIsMalformed_Throws()
        {
            using MemoryStream stream = CreateStream("<assembly>");
            ClickOnceManifestReader reader = new();

            Assert.ThrowsAny<XmlException>(
                () => reader.TryReadApplicationManifest(stream, out _));
        }

        [Fact]
        public void ManifestUtilities_WhenPreservingStream_RetainsPublisherIdentity()
        {
            using FileStream stream = File.OpenRead(GetVstoManifestPath());
            ApplicationManifest manifest = Assert.IsType<ApplicationManifest>(
                ManifestReader.ReadManifest(stream, preserveStream: true));
            using TemporaryFile output = new();

            ManifestWriter.WriteManifest(
                manifest,
                output.File.FullName,
                TargetFrameworkVersion);

            Assert.Single(
                XDocument.Load(output.File.FullName)
                    .Descendants(
                        XName.Get("publisherIdentity", AssemblyV2Namespace)));
        }

        [Fact]
        public void ManifestUtilities_WhenModeledValuesAreCleared_DoesNotRestoreThemFromBase()
        {
            using FileStream stream = File.OpenRead(GetVstoManifestPath());
            ApplicationManifest manifest = Assert.IsType<ApplicationManifest>(
                ManifestReader.ReadManifest(stream, preserveStream: true));
            using TemporaryFile output = new();

            manifest.AssemblyIdentity.PublicKeyToken = null;
            manifest.Description = null;
            manifest.Publisher = null;
            manifest.Product = null;
            manifest.TrustInfo = new TrustInfo
            {
                SameSiteAccess = "none"
            };
            ManifestWriter.WriteManifest(
                manifest,
                output.File.FullName,
                TargetFrameworkVersion);

            XDocument document = XDocument.Load(output.File.FullName);
            XElement identity = Assert.Single(
                document.Descendants(
                    XName.Get("assemblyIdentity", AssemblyV1Namespace)));

            Assert.NotEqual(
                "0011223344556677",
                identity.Attribute("publicKeyToken")?.Value);
            Assert.Empty(
                document.Descendants(
                    XName.Get("description", AssemblyV1Namespace)));
            Assert.DoesNotContain(
                "Old Product",
                document.ToString(),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "SameSite=\"site\"",
                document.ToString(),
                StringComparison.Ordinal);
        }

        [Fact]
        public void ManifestUtilities_WhenDeploymentValuesChange_UsesUpdatedValues()
        {
            const string Xml = """
                <?xml version="1.0" encoding="utf-8"?>
                <asmv1:assembly
                    manifestVersion="1.0"
                    xmlns:asmv1="urn:schemas-microsoft-com:asm.v1"
                    xmlns="urn:schemas-microsoft-com:asm.v2">
                  <asmv1:assemblyIdentity
                      name="TestDeployment"
                      version="1.0.0.0"
                      processorArchitecture="msil"
                      type="win32" />
                  <description xmlns="urn:schemas-microsoft-com:asm.v1" />
                  <deployment
                      install="true"
                      mapFileExtensions="true"
                      minimumRequiredVersion="1.0.0.0"
                      trustURLParameters="true" />
                </asmv1:assembly>
                """;

            using MemoryStream stream = CreateStream(Xml);
            DeployManifest manifest = Assert.IsType<DeployManifest>(
                ManifestReader.ReadManifest(
                    stream,
                    preserveStream: true));
            using TemporaryFile output = new();

            manifest.Install = false;
            manifest.MapFileExtensions = false;
            manifest.MinimumRequiredVersion = null;
            manifest.TrustUrlParameters = false;
            ManifestWriter.WriteManifest(
                manifest,
                output.File.FullName,
                TargetFrameworkVersion);

            XElement deployment = Assert.Single(
                XDocument.Load(output.File.FullName)
                    .Descendants(
                        XName.Get("deployment", AssemblyV2Namespace)));

            Assert.NotEqual("true", deployment.Attribute("install")?.Value);
            Assert.NotEqual(
                "true",
                deployment.Attribute("mapFileExtensions")?.Value);
            Assert.Null(deployment.Attribute("minimumRequiredVersion"));
            Assert.NotEqual(
                "true",
                deployment.Attribute("trustURLParameters")?.Value);
        }

        [Fact]
        public void ApplicationManifestAdapter_UpdateFileInfo_UsesSha256()
        {
            string temporaryFilePath = Path.GetTempFileName();

            try
            {
                File.WriteAllText(temporaryFilePath, contents: "payload");
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
            ApplicationManifestAdapter adapter = new(
                CreateApplicationManifest());

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

            ApplicationManifestAdapter adapter = new(
                CreateApplicationManifest());
            adapter.Write(output.File);

            using FileStream stream = File.OpenRead(manifestPath);
            ClickOnceManifestReader reader = new();
            Assert.True(
                reader.TryReadApplicationManifest(
                    stream,
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
            ApplicationManifestAdapter adapter = new(
                CreateApplicationManifest());

            Assert.Throws<DirectoryNotFoundException>(
                () => adapter.Write(output));
            Assert.False(Directory.Exists(output.DirectoryName));
        }

        [Fact]
        public void ApplicationManifestAdapter_Write_WhenFileIsNull_Throws()
        {
            ApplicationManifestAdapter adapter = new(
                CreateApplicationManifest());

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

        private static string CreateApplicationManifestXml(
            string documentType,
            string identityName)
        {
            return $$"""
                <?xml version="1.0" encoding="utf-8"?>
                {{documentType}}
                <asmv1:assembly
                    manifestVersion="1.0"
                    xmlns:asmv1="urn:schemas-microsoft-com:asm.v1"
                    xmlns="urn:schemas-microsoft-com:asm.v2">
                  <asmv1:assemblyIdentity
                      name="{{identityName}}"
                      version="1.0.0.0"
                      processorArchitecture="msil"
                      type="win32" />
                  <application />
                  <entryPoint>
                    <customHostSpecified xmlns="urn:schemas-microsoft-com:clickonce.v1" />
                  </entryPoint>
                </asmv1:assembly>
                """;
        }

        private static void AssertVstoXmlPreserved(
            XDocument expected,
            XDocument actual)
        {
            XElement expectedExtension = Assert.Single(
                expected.Descendants(
                    XName.Get("addIn", VstaV3Namespace)));
            XElement actualExtension = Assert.Single(
                actual.Descendants(
                    XName.Get("addIn", VstaV3Namespace)));

            Assert.True(
                XNode.DeepEquals(
                    NormalizeElement(expectedExtension),
                    NormalizeElement(actualExtension)));
            Assert.Empty(
                actual.Descendants(
                    XName.Get(
                        "publisherIdentity",
                        AssemblyV2Namespace)));
            Assert.Empty(
                actual.Descendants(
                    XName.Get("Signature", SignatureNamespace)));
        }

        private static MemoryStream CreateStream(string xml)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(xml));
        }

        private static string GetVstoManifestPath()
        {
            return Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "ClickOnce",
                "VstoApplication.manifest");
        }

        private static XElement NormalizeElement(XElement element)
        {
            return new XElement(
                element.Name,
                element.Attributes()
                    .Where(attribute => !attribute.IsNamespaceDeclaration)
                    .OrderBy(attribute => attribute.Name.NamespaceName)
                    .ThenBy(attribute => attribute.Name.LocalName),
                element.Nodes()
                    .Where(node => node is not XText text ||
                        !string.IsNullOrWhiteSpace(text.Value))
                    .Select(node => node is XElement child
                        ? NormalizeElement(child)
                        : node));
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

        private sealed class NonSeekableReadStream : Stream
        {
            private readonly Stream _innerStream;

            internal NonSeekableReadStream(Stream innerStream)
            {
                _innerStream = innerStream;
            }

            public override bool CanRead => _innerStream.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _innerStream.Length;

            public override long Position
            {
                get => _innerStream.Position;
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(
                byte[] buffer,
                int offset,
                int count)
            {
                return _innerStream.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(
                byte[] buffer,
                int offset,
                int count)
            {
                throw new NotSupportedException();
            }
        }
    }
}
