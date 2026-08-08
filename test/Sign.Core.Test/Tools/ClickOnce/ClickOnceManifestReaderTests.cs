// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Text;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core.Test
{
    public sealed class ClickOnceManifestReaderTests
    {
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
        public void ApplicationManifestAdapter_UpdateFileInfo_UsesRequestedTargetFramework()
        {
            const int Sha256HashSizeInBytes = 32;
            const string TargetFrameworkVersion = "v4.5";
            string temporaryFilePath = Path.GetTempFileName();

            try
            {
                File.WriteAllText(temporaryFilePath, "payload");
                ApplicationManifest applicationManifest = CreateApplicationManifest();
                FileReference reference = new(temporaryFilePath);
                applicationManifest.FileReferences.Add(reference);
                ApplicationManifestAdapter adapter = new(applicationManifest);

                adapter.ResolveFiles(new[] { Path.GetDirectoryName(temporaryFilePath)! });
                adapter.UpdateFileInfo(TargetFrameworkVersion);

                Assert.Equal(Sha256HashSizeInBytes, Convert.FromBase64String(reference.Hash).Length);
                Assert.Equal(new FileInfo(temporaryFilePath).Length, reference.Size);
            }
            finally
            {
                File.Delete(temporaryFilePath);
            }
        }

        [Fact]
        public void ApplicationManifestAdapter_Write_RoundTripsUpdatedManifest()
        {
            ApplicationManifestAdapter adapter = new(CreateApplicationManifest());
            using MemoryStream stream = new();

            adapter.Write(stream);
            stream.Position = 0;

            ClickOnceManifestReader reader = new();
            Assert.True(
                reader.TryReadApplicationManifest(
                    stream,
                    preserveStream: true,
                    out IApplicationManifest? manifest));
            Assert.NotNull(manifest);
            Assert.Equal("TestApplication", manifest.AssemblyIdentity.Name);
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
    }
}
