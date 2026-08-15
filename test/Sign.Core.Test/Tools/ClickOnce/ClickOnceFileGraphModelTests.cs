// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using NSubstitute;

namespace Sign.Core.Test
{
    public sealed class ClickOnceFileGraphModelTests
    {
        private const string ApplicationManifestFileName = "App.exe.manifest";
        private const string DeploySuffix = ".deploy";
        private const string DeploymentManifestFileName = "App.application";
        private const string PayloadFileName = "payload.dll";
        private const string ResolutionFailedMessage = "Resolution failed.";
        private const string SetupFileName = "setup.exe";
        private const string WarningMessageName = "GenerateManifest.ResolveFailedInReadWriteMode";
        private const string WarningTargetPath = "warning.txt";
        private const string Whitespace = " ";

        [Fact]
        public void FileGraphEntry_Constructor_PreservesValues()
        {
            FileInfo source = new(fileName: $"{PayloadFileName}{DeploySuffix}");
            FileReference manifestReference = new();

            ClickOnceFileGraphEntry entry = new(
                source,
                PayloadFileName,
                ClickOnceFileGraphEntryKind.Payload,
                manifestReference,
                DeploySuffix);

            Assert.Same(source, entry.Source);
            Assert.Equal(PayloadFileName, entry.TargetPath);
            Assert.Equal(ClickOnceFileGraphEntryKind.Payload, entry.Kind);
            Assert.Same(manifestReference, entry.ManifestReference);
            Assert.Equal(DeploySuffix, entry.MappingAddedSuffix);
        }

        [Fact]
        public void FileGraphEntry_Constructor_ValidatesRequiredArguments()
        {
            FileInfo source = new(fileName: PayloadFileName);

            Assert.Throws<ArgumentNullException>(
                () => new ClickOnceFileGraphEntry(
                    source: null!,
                    targetPath: PayloadFileName,
                    kind: ClickOnceFileGraphEntryKind.Payload));
            Assert.Throws<ArgumentException>(
                () => new ClickOnceFileGraphEntry(
                    source,
                    targetPath: Whitespace,
                    kind: ClickOnceFileGraphEntryKind.Payload));
        }

        [Fact]
        public void FileGraph_Constructor_PreservesModelsAndSnapshotsCollections()
        {
            ClickOnceFileGraphEntry deploymentManifest = CreateEntry(
                DeploymentManifestFileName,
                ClickOnceFileGraphEntryKind.DeploymentManifest);
            ClickOnceFileGraphEntry applicationManifest = CreateEntry(
                ApplicationManifestFileName,
                ClickOnceFileGraphEntryKind.ApplicationManifest);
            ClickOnceFileGraphEntry payload = CreateEntry(
                PayloadFileName,
                ClickOnceFileGraphEntryKind.Payload);
            ClickOnceFileGraphEntry adjacentExecutable = CreateEntry(
                SetupFileName,
                ClickOnceFileGraphEntryKind.Setup);
            IDeployManifest deployManifest = Substitute.For<IDeployManifest>();
            IApplicationManifest applicationManifestModel =
                Substitute.For<IApplicationManifest>();
            List<ClickOnceFileGraphEntry> payloads = new() { payload };
            List<ClickOnceFileGraphEntry> adjacentExecutables = new() { adjacentExecutable };
            List<ClickOnceManifestDiagnostic> diagnostics = new() { CreateDiagnostic() };

            ClickOnceFileGraph graph = new(
                deploymentManifest,
                deployManifest,
                applicationManifest,
                applicationManifestModel,
                payloads,
                adjacentExecutables,
                diagnostics);

            payloads.Clear();
            adjacentExecutables.Clear();
            diagnostics.Clear();

            Assert.Same(deploymentManifest, graph.DeploymentManifest);
            Assert.Same(deployManifest, graph.DeployManifest);
            Assert.Same(applicationManifest, graph.ApplicationManifest);
            Assert.Same(applicationManifestModel, graph.ApplicationManifestModel);
            Assert.Same(payload, Assert.Single(graph.Payloads));
            Assert.Same(adjacentExecutable, Assert.Single(graph.AdjacentExecutables));
            Assert.Single(graph.Diagnostics);
        }

        [Fact]
        public void FileGraph_Constructor_ValidatesRequiredArguments()
        {
            ClickOnceFileGraphEntry applicationManifest = CreateEntry(
                ApplicationManifestFileName,
                ClickOnceFileGraphEntryKind.ApplicationManifest);
            IApplicationManifest applicationManifestModel =
                Substitute.For<IApplicationManifest>();
            ClickOnceFileGraphEntry[] entries = Array.Empty<ClickOnceFileGraphEntry>();
            ClickOnceManifestDiagnostic[] diagnostics = Array.Empty<ClickOnceManifestDiagnostic>();

            Assert.Throws<ArgumentNullException>(
                () => new ClickOnceFileGraph(
                    deploymentManifest: null,
                    deployManifest: null,
                    applicationManifest: null!,
                    applicationManifestModel,
                    payloads: entries,
                    adjacentExecutables: entries,
                    diagnostics));
            Assert.Throws<ArgumentNullException>(
                () => new ClickOnceFileGraph(
                    deploymentManifest: null,
                    deployManifest: null,
                    applicationManifest,
                    applicationManifestModel: null!,
                    payloads: entries,
                    adjacentExecutables: entries,
                    diagnostics));
            Assert.Throws<ArgumentNullException>(
                () => new ClickOnceFileGraph(
                    deploymentManifest: null,
                    deployManifest: null,
                    applicationManifest,
                    applicationManifestModel,
                    payloads: null!,
                    adjacentExecutables: entries,
                    diagnostics));
            Assert.Throws<ArgumentNullException>(
                () => new ClickOnceFileGraph(
                    deploymentManifest: null,
                    deployManifest: null,
                    applicationManifest,
                    applicationManifestModel,
                    payloads: entries,
                    adjacentExecutables: null!,
                    diagnostics));
            Assert.Throws<ArgumentNullException>(
                () => new ClickOnceFileGraph(
                    deploymentManifest: null,
                    deployManifest: null,
                    applicationManifest,
                    applicationManifestModel,
                    payloads: entries,
                    adjacentExecutables: entries,
                    diagnostics: null!));
        }

        [Fact]
        public void FileGraphResolutionException_Constructor_PreservesValuesAndSnapshotsDiagnostics()
        {
            ClickOnceManifestDiagnostic diagnostic = CreateDiagnostic();
            List<ClickOnceManifestDiagnostic> diagnostics = new() { diagnostic };
            InvalidOperationException innerException = new();

            ClickOnceFileGraphResolutionException exception = new(
                ResolutionFailedMessage,
                diagnostics,
                innerException);

            diagnostics.Clear();

            Assert.Equal(ResolutionFailedMessage, exception.Message);
            Assert.Same(diagnostic, Assert.Single(exception.Diagnostics));
            Assert.Same(innerException, exception.InnerException);
        }

        [Fact]
        public void FileGraphResolutionException_Constructor_DefaultsDiagnosticsAndValidatesMessage()
        {
            ClickOnceFileGraphResolutionException exception = new(message: ResolutionFailedMessage);

            Assert.Empty(exception.Diagnostics);
            Assert.Throws<ArgumentException>(
                () => new ClickOnceFileGraphResolutionException(message: Whitespace));
        }

        [Fact]
        public void ManifestDiagnostic_Constructor_PreservesValues()
        {
            ClickOnceManifestDiagnostic diagnostic = new(
                WarningMessageName,
                WarningTargetPath,
                OutputMessageType.Warning);

            Assert.Equal(WarningMessageName, diagnostic.Name);
            Assert.Equal(WarningTargetPath, diagnostic.Text);
            Assert.Equal(OutputMessageType.Warning, diagnostic.Type);
        }

        [Fact]
        public void ManifestDiagnostic_Constructor_ValidatesMessage()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ClickOnceManifestDiagnostic(message: null!));
        }

        private static ClickOnceFileGraphEntry CreateEntry(
            string path,
            ClickOnceFileGraphEntryKind kind)
        {
            return new ClickOnceFileGraphEntry(new FileInfo(path), path, kind);
        }

        private static ClickOnceManifestDiagnostic CreateDiagnostic()
        {
            return new ClickOnceManifestDiagnostic(
                WarningMessageName,
                WarningTargetPath,
                OutputMessageType.Warning);
        }
    }
}
