// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using NSubstitute;

namespace Sign.Core.Test
{
    public sealed class ResolvedClickOncePublishLayoutTests
    {
        private const string ApplicationManifestFileName = "App.exe.manifest";
        private const string DeploymentManifestFileName = "App.application";
        private const string SetupFileName = "setup.exe";
        private const string WarningMessageName = "GenerateManifest.ResolveFailedInReadWriteMode";
        private const string WarningTargetPath = "warning.txt";

        [Fact]
        public void Constructor_WithValidArguments_PreservesValuesAndSnapshotsCollections()
        {
            FileInfo deploymentManifest = new(DeploymentManifestFileName);
            ResolvedClickOnceAdjacentExecutable adjacentExecutable = new(
                new FileInfo(SetupFileName),
                ClickOnceAdjacentExecutableKind.Setup);
            IDeployManifest deployManifest = Substitute.For<IDeployManifest>();
            List<ResolvedClickOnceAdjacentExecutable> adjacentExecutables = new() { adjacentExecutable };
            List<ClickOnceManifestDiagnostic> diagnostics = new() { CreateDiagnostic() };
            AssemblyReference applicationManifestReference = new();
            deployManifest.EntryPoint.Returns(applicationManifestReference);
            ResolvedClickOnceDeployment deployment = new(
                deploymentManifest,
                deployManifest,
                applicationManifestReference);
            ResolvedClickOnceApplication application = CreateApplication();

            ResolvedClickOncePublishLayout layout = new(
                deployment,
                application,
                adjacentExecutables,
                diagnostics);

            adjacentExecutables.Clear();
            diagnostics.Clear();

            Assert.Same(deployment, layout.Deployment);
            Assert.Same(application, layout.Application);
            Assert.Same(adjacentExecutable, Assert.Single(layout.AdjacentExecutables));
            Assert.Single(layout.Diagnostics);
        }

        [Fact]
        public void Constructor_WithoutDeployment_PreservesNullDeployment()
        {
            ResolvedClickOncePublishLayout layout = CreateLayout();

            Assert.Null(layout.Deployment);
        }

        [Fact]
        public void Constructor_WithoutDeploymentAndWithAdjacentExecutables_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(
                () => new ResolvedClickOncePublishLayout(
                    deployment: null,
                    CreateApplication(),
                    adjacentExecutables:
                    [
                        new ResolvedClickOnceAdjacentExecutable(
                            new FileInfo(SetupFileName),
                            ClickOnceAdjacentExecutableKind.Setup)
                    ],
                    diagnostics: Array.Empty<ClickOnceManifestDiagnostic>()));
        }

        [Fact]
        public void Constructor_WithNullApplication_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ResolvedClickOncePublishLayout(
                    deployment: null,
                    application: null!,
                    adjacentExecutables: Array.Empty<ResolvedClickOnceAdjacentExecutable>(),
                    diagnostics: Array.Empty<ClickOnceManifestDiagnostic>()));
        }

        [Fact]
        public void Constructor_WithNullAdjacentExecutables_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ResolvedClickOncePublishLayout(
                    deployment: null,
                    CreateApplication(),
                    adjacentExecutables: null!,
                    diagnostics: Array.Empty<ClickOnceManifestDiagnostic>()));
        }

        [Fact]
        public void Constructor_WithNullDiagnostics_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ResolvedClickOncePublishLayout(
                    deployment: null,
                    CreateApplication(),
                    adjacentExecutables: Array.Empty<ResolvedClickOnceAdjacentExecutable>(),
                    diagnostics: null!));
        }

        private static ResolvedClickOncePublishLayout CreateLayout()
        {
            return new ResolvedClickOncePublishLayout(
                deployment: null,
                CreateApplication(),
                adjacentExecutables: Array.Empty<ResolvedClickOnceAdjacentExecutable>(),
                diagnostics: Array.Empty<ClickOnceManifestDiagnostic>());
        }

        private static ResolvedClickOnceApplication CreateApplication()
        {
            return new ResolvedClickOnceApplication(
                new FileInfo(ApplicationManifestFileName),
                Substitute.For<IApplicationManifest>(),
                payloads: Array.Empty<ResolvedClickOncePayload>());
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
