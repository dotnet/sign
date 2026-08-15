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
                ClickOnceFileGraphTestUtilities.CreateApplicationManifest();

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
        public void DeploymentResolver_WhenFallbackSucceeds_PreservesRealDeploymentThenApplicationDiagnosticOrder()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo payload =
                ClickOnceFileGraphTestUtilities.CreateFile(
                    root,
                    FallbackPayloadTargetPath);
            FileInfo applicationManifest =
                ClickOnceFileGraphTestUtilities.WriteApplicationManifest(
                    root,
                    $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                    FallbackPayloadTargetPath);
            FileInfo deploymentManifest =
                ClickOnceFileGraphTestUtilities.WriteDeploymentManifest(
                    root,
                    DeploymentManifestFileName,
                    Path.GetRelativePath(
                        root.FullName,
                        applicationManifest.FullName));
            ClickOnceDeployManifestFileGraphResolver resolver =
                CreateResolverWithDeploymentDiagnostic(
                    applicationManifest,
                    DeploymentWarningTargetPath);

            ClickOnceFileGraph graph = resolver.Resolve(deploymentManifest);

            Assert.Equal(
                payload.FullName,
                Assert.Single(graph.Payloads).Source.FullName);
            Assert.Collection(
                graph.Diagnostics,
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
        public void DeploymentResolver_WhenFallbackCannotResolveEveryPayload_PreservesRealCrossManifestDiagnosticOrder()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;

            ClickOnceFileGraphTestUtilities.CreateFile(
                root,
                FallbackPayloadTargetPath);
            FileInfo applicationManifest =
                ClickOnceFileGraphTestUtilities.WriteApplicationManifest(
                    root,
                    $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                    FallbackPayloadTargetPath,
                    MissingPayloadTargetPath);
            FileInfo deploymentManifest =
                ClickOnceFileGraphTestUtilities.WriteDeploymentManifest(
                    root,
                    DeploymentManifestFileName,
                    Path.GetRelativePath(
                        root.FullName,
                        applicationManifest.FullName));
            ClickOnceDeployManifestFileGraphResolver resolver =
                CreateResolverWithDeploymentDiagnostic(
                    applicationManifest,
                    DeploymentWarningTargetPath);

            ClickOnceFileGraphResolutionException exception =
                Assert.Throws<ClickOnceFileGraphResolutionException>(
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

        private static ClickOnceDeployManifestFileGraphResolver CreateResolverWithDeploymentDiagnostic(
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

            return new ClickOnceDeployManifestFileGraphResolver(
                manifestReader,
                new ClickOncePayloadFileResolver());
        }
    }
}
