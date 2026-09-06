// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Xml.Linq;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed class ClickOnceVstoManifestResolutionTests : IDisposable
    {
        private const string ApplicationDirectory =
            @"Application Files\VstoTestAddIn_1_0_0_0";
        private const string ApplicationManifestFileName =
            "VstoApplication.manifest";
        private const string DeploymentManifestFileName =
            "VstoTestAddIn.vsto";
        private const string PayloadFileName = "payload.dll";
        private const string VstaV3Namespace =
            "urn:schemas-microsoft-com:vsta.v3";
        private const string VstoV4Namespace =
            "urn:schemas-microsoft-com:vsto.v4";

        private readonly DirectoryService _directoryService;

        public ClickOnceVstoManifestResolutionTests()
        {
            _directoryService = new(
                Substitute.For<ILogger<IDirectoryService>>());
        }

        public void Dispose()
        {
            _directoryService.Dispose();
        }

        [Fact]
        public void DeploymentPublishLayoutResolver_WhenApplicationIsRealVstoFixture_ResolvesPublishLayoutAndPreservesModelState()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo applicationManifestFile = new(
                Path.Combine(
                    root.FullName,
                    ApplicationDirectory,
                    ApplicationManifestFileName));
            applicationManifestFile.Directory!.Create();
            File.Copy(
                ClickOnceResolutionTestUtilities.GetVstoManifestPath(),
                applicationManifestFile.FullName);
            FileInfo payload = new(
                Path.Combine(
                    applicationManifestFile.DirectoryName!,
                    PayloadFileName));
            File.Copy(
                typeof(ClickOnceVstoManifestResolutionTests).Assembly.Location,
                payload.FullName);
            FileInfo deploymentManifestFile =
                ClickOnceResolutionTestUtilities.WriteDeploymentManifest(
                    root,
                    DeploymentManifestFileName,
                    Path.GetRelativePath(
                        root.FullName,
                        applicationManifestFile.FullName));
            applicationManifestFile.Refresh();
            payload.Refresh();

            ClickOnceDeploymentPublishLayoutResolver resolver = new(
                new ClickOnceManifestReader(),
                new ClickOncePayloadResolver());

            ResolvedClickOncePublishLayout layout =
                resolver.Resolve(deploymentManifestFile);

            Assert.Equal(
                deploymentManifestFile.FullName,
                layout.Deployment!.Source.FullName);
            Assert.NotNull(layout.Deployment!.Manifest);
            Assert.Equal(
                applicationManifestFile.FullName,
                layout.Application.Source.FullName);
            Assert.Equal(
                "VstoTestAddIn.dll",
                layout.Application.Manifest.AssemblyIdentity.Name);
            Assert.False(layout.Application.Manifest.ReadOnly);
            Assert.Empty(layout.Diagnostics);

            ResolvedClickOncePayload payloadEntry =
                Assert.Single(layout.Application.Payloads);
            AssemblyReference manifestReference =
                Assert.IsType<AssemblyReference>(
                    payloadEntry.Reference);

            Assert.Equal(payload.FullName, payloadEntry.Source.FullName);
            Assert.Equal(PayloadFileName, payloadEntry.TargetPath);
            Assert.Equal(
                payload.FullName,
                manifestReference.ResolvedPath);
            Assert.Equal(
                "VstoTestAddIn",
                manifestReference.AssemblyIdentity.Name);
            Assert.Same(
                manifestReference,
                Assert.Single(
                    layout.Application.Manifest
                        .AssemblyReferences
                        .Cast<AssemblyReference>()));

            FileInfo output = new(
                Path.Combine(
                    root.FullName,
                    "roundtrip.manifest"));

            layout.Application.Manifest.Write(output);

            XDocument document = XDocument.Load(output.FullName);
            XElement addIn = Assert.Single(
                document.Descendants(
                    XName.Get("addIn", VstaV3Namespace)));
            XElement appAddIn = Assert.Single(
                addIn.Descendants(
                    XName.Get("appAddIn", VstoV4Namespace)));

            Assert.Equal(
                "VstoTestAddIn",
                appAddIn.Attribute("keyName")?.Value);
        }
    }
}
