// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using System.Xml;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed class ClickOnceResolutionFailureTests : IDisposable
    {
        private const string ApplicationManifestFileName =
            "App.exe.manifest";
        private const string DeploymentManifestFileName =
            "App.application";
        private const string PayloadFileName = "payload.dll";
        private const string SourcePath = "source-path.dll";
        private const string WarningMessageName =
            "GenerateManifest.ResolveFailedInReadWriteMode";
        private const string WarningTargetPath = "warning.txt";

        private readonly DirectoryService _directoryService;

        public ClickOnceResolutionFailureTests()
        {
            _directoryService = new(
                Substitute.For<ILogger<IDirectoryService>>());
        }

        public void Dispose()
        {
            _directoryService.Dispose();
        }

        [Fact]
        public void PayloadResolver_WhenResolveFilesThrows_RestoresHintsClearsResolvedPathAndPreservesDiagnostics()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo applicationManifestFile =
                ClickOnceFileGraphTestUtilities.CreateFile(
                    root,
                    ApplicationManifestFileName);
            ApplicationManifest applicationModel =
                ClickOnceFileGraphTestUtilities.CreateApplicationManifest();
            AssemblyIdentity identity = new(
                "Payload",
                ClickOnceFileGraphTestUtilities.ManifestVersion);
            AssemblyReference reference = new()
            {
                AssemblyIdentity = identity,
                ResolvedPath = "original-resolved-path.dll",
                SourcePath = SourcePath,
                TargetPath = PayloadFileName
            };

            applicationModel.AssemblyReferences.Add(reference);

            List<ClickOnceManifestDiagnostic> manifestDiagnostics = new();
            ClickOnceManifestDiagnostic expectedDiagnostic =
                CreateWarningDiagnostic();
            IOException expectedException = new();
            IApplicationManifest applicationManifest =
                Substitute.For<IApplicationManifest>();

            applicationManifest.AssemblyReferences.Returns(
                applicationModel.AssemblyReferences);
            applicationManifest.EntryPoint.Returns(
                applicationModel.EntryPoint);
            applicationManifest.FileReferences.Returns(
                applicationModel.FileReferences);
            applicationManifest.Diagnostics.Returns(manifestDiagnostics);
            applicationManifest
                .When(manifest => manifest.ResolveFiles(
                    Arg.Any<IReadOnlyList<DirectoryInfo>>()))
                .Do(_ =>
                {
                    Assert.Null(reference.SourcePath);
                    Assert.Null(reference.AssemblyIdentity);
                    reference.ResolvedPath = "partial-resolved-path.dll";
                    manifestDiagnostics.Add(expectedDiagnostic);

                    throw expectedException;
                });

            ClickOncePayloadFileResolver payloadResolver = new();
            List<ClickOnceManifestDiagnostic> diagnostics = new();

            ClickOnceFileGraphResolutionException exception =
                Assert.Throws<ClickOnceFileGraphResolutionException>(
                    () => payloadResolver.ResolveForExplicitApplication(
                        applicationManifestFile,
                        applicationManifest,
                        diagnostics));

            Assert.Equal(SourcePath, reference.SourcePath);
            Assert.Same(identity, reference.AssemblyIdentity);
            Assert.Null(reference.ResolvedPath);
            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceApplicationManifestResolveFailed,
                    applicationManifestFile.FullName),
                exception.Message);
            Assert.Same(expectedException, exception.InnerException);
            Assert.Same(
                expectedDiagnostic,
                Assert.Single(exception.Diagnostics));
            Assert.Same(
                expectedDiagnostic,
                Assert.Single(diagnostics));
        }

        [Fact]
        public void DeploymentResolver_WhenResolveFilesThrows_RestoresHintsPreservesResolvedPathAndDiagnostics()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo deploymentManifestFile =
                ClickOnceFileGraphTestUtilities.CreateFile(
                    root,
                    DeploymentManifestFileName);
            AssemblyIdentity identity = new(
                "Application",
                ClickOnceFileGraphTestUtilities.ManifestVersion);
            AssemblyReference entryPoint = new()
            {
                AssemblyIdentity = identity,
                SourcePath = SourcePath,
                TargetPath = ApplicationManifestFileName
            };
            DeployManifest deploymentModel = new();

            deploymentModel.AssemblyReferences.Add(entryPoint);
            deploymentModel.EntryPoint = entryPoint;

            List<ClickOnceManifestDiagnostic> diagnostics = new();
            ClickOnceManifestDiagnostic expectedDiagnostic =
                CreateWarningDiagnostic();
            UnauthorizedAccessException expectedException = new();
            IDeployManifest deploymentManifest =
                Substitute.For<IDeployManifest>();

            deploymentManifest.AssemblyReferences.Returns(
                deploymentModel.AssemblyReferences);
            deploymentManifest.EntryPoint.Returns(entryPoint);
            deploymentManifest.FileReferences.Returns(
                deploymentModel.FileReferences);
            deploymentManifest.Diagnostics.Returns(diagnostics);
            deploymentManifest
                .When(manifest => manifest.ResolveFiles(
                    Arg.Any<IReadOnlyList<DirectoryInfo>>()))
                .Do(_ =>
                {
                    Assert.Null(entryPoint.SourcePath);
                    Assert.Null(entryPoint.AssemblyIdentity);
                    entryPoint.ResolvedPath =
                        "partial-application-manifest-path";
                    diagnostics.Add(expectedDiagnostic);

                    throw expectedException;
                });

            IClickOnceManifestReader manifestReader =
                Substitute.For<IClickOnceManifestReader>();

            manifestReader.TryReadDeployManifest(
                    Arg.Any<Stream>(),
                    out Arg.Any<IDeployManifest?>())
                .Returns(callInfo =>
                {
                    callInfo[1] = deploymentManifest;
                    return true;
                });

            ClickOnceDeployManifestFileGraphResolver resolver = new(
                manifestReader,
                new ClickOncePayloadFileResolver());

            ClickOnceFileGraphResolutionException exception =
                Assert.Throws<ClickOnceFileGraphResolutionException>(
                    () => resolver.Resolve(deploymentManifestFile));

            Assert.Equal(SourcePath, entryPoint.SourcePath);
            Assert.Same(identity, entryPoint.AssemblyIdentity);
            Assert.Equal(
                "partial-application-manifest-path",
                entryPoint.ResolvedPath);
            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceDeploymentManifestResolveFailed,
                    deploymentManifestFile.FullName),
                exception.Message);
            Assert.Same(expectedException, exception.InnerException);
            Assert.Same(
                expectedDiagnostic,
                Assert.Single(exception.Diagnostics));
        }

        [Fact]
        public void ExplicitApplicationResolver_WhenManifestIsMalformed_ThrowsReadFailureWithXmlException()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            FileInfo applicationManifestFile =
                ClickOnceFileGraphTestUtilities.WriteMalformedManifest(
                    temporaryDirectory.Directory,
                    ApplicationManifestFileName);
            ClickOnceApplicationManifestFileGraphResolver resolver = new(
                new ClickOnceManifestReader(),
                new ClickOncePayloadFileResolver());

            ClickOnceFileGraphResolutionException exception =
                Assert.Throws<ClickOnceFileGraphResolutionException>(
                    () => resolver.TryResolve(
                        applicationManifestFile,
                        out _));

            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceApplicationManifestReadFailed,
                    applicationManifestFile.FullName),
                exception.Message);
            Assert.IsType<XmlException>(exception.InnerException);
            Assert.Empty(exception.Diagnostics);
        }

        [Fact]
        public void ExplicitApplicationResolver_WhenManifestIsMissing_ThrowsReadFailureWithFileNotFoundException()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            FileInfo applicationManifestFile = new(
                Path.Combine(
                    temporaryDirectory.Directory.FullName,
                    ApplicationManifestFileName));
            ClickOnceApplicationManifestFileGraphResolver resolver = new(
                new ClickOnceManifestReader(),
                new ClickOncePayloadFileResolver());

            ClickOnceFileGraphResolutionException exception =
                Assert.Throws<ClickOnceFileGraphResolutionException>(
                    () => resolver.TryResolve(
                        applicationManifestFile,
                        out _));

            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceApplicationManifestReadFailed,
                    applicationManifestFile.FullName),
                exception.Message);
            Assert.IsType<FileNotFoundException>(exception.InnerException);
            Assert.Empty(exception.Diagnostics);
        }

        private static ClickOnceManifestDiagnostic CreateWarningDiagnostic()
        {
            return new ClickOnceManifestDiagnostic(
                WarningMessageName,
                WarningTargetPath,
                OutputMessageType.Warning);
        }
    }
}
