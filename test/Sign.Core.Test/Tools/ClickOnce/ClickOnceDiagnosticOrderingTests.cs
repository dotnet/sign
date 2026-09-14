// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed class ClickOnceDiagnosticOrderingTests : IDisposable
    {
        private const string ApplicationDirectory =
            @"Application Files\App_1_0_0_0";
        private const string ApplicationManifestFileName =
            "App.exe.manifest";
        private const string DeploymentManifestFileName =
            "App.application";
        private const string DeploymentWarningTargetPath =
            "deployment-warning.dll";
        private const string FallbackPayloadTargetPath =
            "fallback.dll";
        private const string MissingPayloadTargetPath =
            "missing.dll";

        private readonly DirectoryService _directoryService;

        public ClickOnceDiagnosticOrderingTests()
        {
            _directoryService = new(
                Substitute.For<ILogger<IDirectoryService>>());
        }

        public void Dispose()
        {
            _directoryService.Dispose();
        }

        [Fact]
        public void ApplicationManifestAdapter_AdaptsMultipleRealOutputMessagesInOrder()
        {
            ApplicationManifest manifest =
                ClickOnceResolutionTestUtilities.CreateApplicationManifest();

            manifest.FileReferences.Add(
                new FileReference()
                {
                    TargetPath = FallbackPayloadTargetPath
                });
            manifest.FileReferences.Add(
                new FileReference()
                {
                    TargetPath = MissingPayloadTargetPath
                });

            ApplicationManifestAdapter adapter = new(manifest);

            adapter.ResolveFiles(Array.Empty<DirectoryInfo>());

            Assert.Collection(
                adapter.Diagnostics,
                diagnostic =>
                    AssertDiagnostic(
                        diagnostic,
                        FallbackPayloadTargetPath),
                diagnostic =>
                    AssertDiagnostic(
                        diagnostic,
                        MissingPayloadTargetPath));
        }

        [Fact]
        public void DeploymentPublishLayoutResolver_WhenFallbackSucceeds_PreservesRealDeploymentThenApplicationDiagnosticOrder()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo payload =
                ClickOnceResolutionTestUtilities.CreateFile(
                    root,
                    FallbackPayloadTargetPath);
            FileInfo applicationManifest =
                ClickOnceResolutionTestUtilities.WriteApplicationManifest(
                    root,
                    $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                    FallbackPayloadTargetPath);
            FileInfo deploymentManifest =
                ClickOnceResolutionTestUtilities.WriteDeploymentManifest(
                    root,
                    DeploymentManifestFileName,
                    Path.GetRelativePath(
                        root.FullName,
                        applicationManifest.FullName));
            ClickOnceDeploymentPublishLayoutResolver resolver =
                CreateDeploymentPublishLayoutResolverWithDiagnostic(
                    applicationManifest,
                    DeploymentWarningTargetPath);

            ResolvedClickOncePublishLayout layout = resolver.Resolve(deploymentManifest);

            Assert.Equal(
                payload.FullName,
                Assert.Single(layout.Application.Payloads).Source.FullName);
            Assert.Collection(
                layout.Diagnostics,
                diagnostic =>
                    AssertDiagnostic(
                        diagnostic,
                        DeploymentWarningTargetPath),
                diagnostic =>
                    AssertDiagnostic(
                        diagnostic,
                        FallbackPayloadTargetPath));
        }

        [Fact]
        public void DeploymentPublishLayoutResolver_WhenFallbackCannotResolveEveryPayload_PreservesRealCrossManifestDiagnosticOrder()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;

            ClickOnceResolutionTestUtilities.CreateFile(
                root,
                FallbackPayloadTargetPath);
            FileInfo applicationManifest =
                ClickOnceResolutionTestUtilities.WriteApplicationManifest(
                    root,
                    $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                    FallbackPayloadTargetPath,
                    MissingPayloadTargetPath);
            FileInfo deploymentManifest =
                ClickOnceResolutionTestUtilities.WriteDeploymentManifest(
                    root,
                    DeploymentManifestFileName,
                    Path.GetRelativePath(
                        root.FullName,
                        applicationManifest.FullName));
            ClickOnceDeploymentPublishLayoutResolver resolver =
                CreateDeploymentPublishLayoutResolverWithDiagnostic(
                    applicationManifest,
                    DeploymentWarningTargetPath);

            ClickOncePublishLayoutResolutionException exception =
                Assert.Throws<ClickOncePublishLayoutResolutionException>(
                    () => resolver.Resolve(deploymentManifest));

            Assert.Contains(
                MissingPayloadTargetPath,
                exception.Message,
                StringComparison.Ordinal);
            Assert.Collection(
                exception.Diagnostics,
                diagnostic =>
                    AssertDiagnostic(
                        diagnostic,
                        DeploymentWarningTargetPath),
                diagnostic =>
                    AssertDiagnostic(
                        diagnostic,
                        FallbackPayloadTargetPath),
                diagnostic =>
                    AssertDiagnostic(
                        diagnostic,
                        MissingPayloadTargetPath),
                diagnostic =>
                    AssertDiagnostic(
                        diagnostic,
                        MissingPayloadTargetPath));
        }

        private static void AssertDiagnostic(
            ClickOnceManifestDiagnostic diagnostic,
            string targetPath)
        {
            Assert.Equal(
                "GenerateManifest.ResolveFailedInReadWriteMode",
                diagnostic.Name);
            Assert.Equal(OutputMessageType.Error, diagnostic.Type);
            Assert.Contains(
                targetPath,
                diagnostic.Text,
                StringComparison.Ordinal);
        }

        private static ClickOnceDeploymentPublishLayoutResolver CreateDeploymentPublishLayoutResolverWithDiagnostic(
            FileInfo applicationManifestFile,
            string deploymentDiagnosticTargetPath)
        {
            AssemblyReference entryPoint = new()
            {
                ResolvedPath = applicationManifestFile.FullName,
                TargetPath = applicationManifestFile.Name
            };
            DeployManifest deploymentModel = new();

            deploymentModel.AssemblyReferences.Add(entryPoint);
            deploymentModel.EntryPoint = entryPoint;

            IDeployManifest deploymentManifest =
                Substitute.For<IDeployManifest>();

            deploymentManifest.AssemblyReferences.Returns(
                deploymentModel.AssemblyReferences);
            deploymentManifest.EntryPoint.Returns(entryPoint);
            deploymentManifest.FileReferences.Returns(
                deploymentModel.FileReferences);
            deploymentManifest.Diagnostics.Returns(
                new[]
                {
                    new ClickOnceManifestDiagnostic(
                        "GenerateManifest.ResolveFailedInReadWriteMode",
                        deploymentDiagnosticTargetPath,
                        OutputMessageType.Error)
                });

            ClickOnceManifestReader realManifestReader = new();
            IApplicationManifest applicationManifest;

            using (FileStream stream = applicationManifestFile.OpenRead())
            {
                Assert.True(
                    realManifestReader.TryReadApplicationManifest(
                        stream,
                        out IApplicationManifest? parsedManifest));
                applicationManifest = Assert.IsAssignableFrom<IApplicationManifest>(
                    parsedManifest);
            }

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
            manifestReader.TryReadApplicationManifest(
                    Arg.Any<Stream>(),
                    out Arg.Any<IApplicationManifest?>())
                .Returns(callInfo =>
                {
                    callInfo[1] = applicationManifest;
                    return true;
                });

            return new ClickOnceDeploymentPublishLayoutResolver(
                manifestReader,
                new ClickOncePayloadResolver());
        }
    }
}
