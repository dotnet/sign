// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed class ClickOnceTargetPathCompatibilityTests : IDisposable
    {
        private const string ApplicationDirectory =
            @"Application Files\App_1_0_0_0";
        private const string ApplicationManifestFileName =
            "App.exe.manifest";
        private const string DeploySuffix = ".deploy";
        private const string DeploymentManifestFileName =
            "App.application";
        private const string PayloadFileName = "payload.dll";

        private readonly DirectoryService _directoryService;

        public ClickOnceTargetPathCompatibilityTests()
        {
            _directoryService = new(
                Substitute.For<ILogger<IDirectoryService>>());
        }

        public static TheoryData<string> SupportedTargetPaths
        {
            get
            {
                string driveRoot = Path.GetPathRoot(
                    Environment.CurrentDirectory)!;
                string drive = driveRoot[..2];

                return new TheoryData<string>()
                {
                    Path.Combine(
                        driveRoot,
                        "published",
                        "rooted.dll"),
                    $@"{drive}published\drive-relative.dll",
                    @"\published\root-relative.dll",
                    @"\\sign-cli.invalid\share\unc.dll",
                    $@"\\?\{driveRoot}published\device.dll",
                    @"..\parent.dll"
                };
            }
        }

        public void Dispose()
        {
            _directoryService.Dispose();
        }

        [Theory]
        [MemberData(nameof(SupportedTargetPaths))]
        public void ExplicitApplicationResolver_PreservesSupportedTargetPathAndWindowsCandidate(
            string targetPath)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo applicationManifestFile =
                ClickOnceFileGraphTestUtilities.CreateFile(
                    root,
                    ApplicationManifestFileName);
            ApplicationManifest applicationModel =
                ClickOnceFileGraphTestUtilities.CreateApplicationManifest();
            FileReference reference = new()
            {
                TargetPath = targetPath
            };

            applicationModel.FileReferences.Add(reference);

            List<string[]> resolutionSearches = new();
            IApplicationManifest applicationManifest =
                CreateApplicationManifestSubstitute(
                    applicationModel,
                    resolutionSearches);
            IClickOnceManifestReader manifestReader =
                Substitute.For<IClickOnceManifestReader>();

            manifestReader.TryReadApplicationManifest(
                    Arg.Any<Stream>(),
                    out Arg.Any<IApplicationManifest?>())
                .Returns(callInfo =>
                {
                    callInfo[1] = applicationManifest;
                    return true;
                });

            List<string> probeCandidates = new();
            ClickOncePayloadFileResolver payloadResolver = new(
                file =>
                {
                    probeCandidates.Add(file.FullName);

                    return true;
                });
            ClickOnceApplicationManifestFileGraphResolver resolver = new(
                manifestReader,
                payloadResolver);

            Assert.True(
                resolver.TryResolve(
                    applicationManifestFile,
                    out ClickOnceFileGraph? graph));

            Assert.NotNull(graph);
            ClickOnceFileGraphEntry payload = Assert.Single(graph.Payloads);
            string expectedCandidate = GetCandidatePath(root, targetPath);

            Assert.Equal(targetPath, payload.TargetPath);
            Assert.Equal(expectedCandidate, payload.Source.FullName);
            Assert.Same(reference, payload.ManifestReference);
            Assert.Equal(
                expectedCandidate,
                Assert.Single(probeCandidates));
            AssertSearchPaths(
                Assert.Single(resolutionSearches),
                root.FullName);
        }

        [Theory]
        [MemberData(nameof(SupportedTargetPaths))]
        public void DeploymentResolver_PreservesSupportedTargetPathAndWindowsCandidate(
            string targetPath)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo deploymentManifestFile =
                ClickOnceFileGraphTestUtilities.CreateFile(
                    root,
                    DeploymentManifestFileName);
            FileInfo applicationManifestFile =
                ClickOnceFileGraphTestUtilities.CreateFile(
                    root,
                    $@"{ApplicationDirectory}\{ApplicationManifestFileName}");
            AssemblyReference deploymentEntryPoint = new()
            {
                ResolvedPath = applicationManifestFile.FullName,
                TargetPath = Path.GetRelativePath(
                    root.FullName,
                    applicationManifestFile.FullName)
            };
            DeployManifest deploymentModel = new();

            deploymentModel.AssemblyReferences.Add(deploymentEntryPoint);
            deploymentModel.EntryPoint = deploymentEntryPoint;

            List<string[]> deploymentResolutionSearches = new();
            IDeployManifest deploymentManifest =
                CreateDeployManifestSubstitute(
                    deploymentModel,
                    deploymentResolutionSearches);

            ApplicationManifest applicationModel =
                ClickOnceFileGraphTestUtilities.CreateApplicationManifest();
            FileReference reference = new()
            {
                TargetPath = targetPath
            };

            applicationModel.FileReferences.Add(reference);

            List<string[]> applicationResolutionSearches = new();
            IApplicationManifest applicationManifest =
                CreateApplicationManifestSubstitute(
                    applicationModel,
                    applicationResolutionSearches);
            IClickOnceManifestReader manifestReader =
                CreateManifestReader(
                    deploymentManifest,
                    applicationManifest);

            List<string> probeCandidates = new();
            ClickOncePayloadFileResolver payloadResolver = new(
                file =>
                {
                    probeCandidates.Add(file.FullName);

                    return true;
                });
            ClickOnceDeployManifestFileGraphResolver resolver = new(
                manifestReader,
                payloadResolver);

            ClickOnceFileGraph graph =
                resolver.Resolve(deploymentManifestFile);

            ClickOnceFileGraphEntry payload = Assert.Single(graph.Payloads);
            string expectedCandidate = GetCandidatePath(
                applicationManifestFile.Directory!,
                targetPath);

            Assert.Equal(targetPath, payload.TargetPath);
            Assert.Equal(expectedCandidate, payload.Source.FullName);
            Assert.Same(reference, payload.ManifestReference);
            Assert.Equal(
                expectedCandidate,
                Assert.Single(probeCandidates));
            AssertSearchPaths(
                Assert.Single(deploymentResolutionSearches),
                root.FullName);
            Assert.Collection(
                applicationResolutionSearches,
                directories =>
                    AssertSearchPaths(
                        directories,
                        applicationManifestFile.DirectoryName!),
                directories =>
                    AssertSearchPaths(
                        directories,
                        applicationManifestFile.DirectoryName!,
                        root.FullName));
        }

        [Fact]
        public void DeploymentResolver_WhenOnlyMappedPayloadExistsInDeploymentDirectory_UsesFallbackAndPreservesFirstAttemptDiagnostic()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo payload =
                ClickOnceFileGraphTestUtilities.CreateFile(
                    root,
                    $"{PayloadFileName}{DeploySuffix}");
            FileInfo applicationManifest =
                ClickOnceFileGraphTestUtilities.WriteApplicationManifest(
                    root,
                    $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                    PayloadFileName);
            FileInfo deploymentManifest =
                ClickOnceFileGraphTestUtilities.WriteDeploymentManifest(
                    root,
                    DeploymentManifestFileName,
                    Path.GetRelativePath(
                        root.FullName,
                        applicationManifest.FullName),
                    mapFileExtensions: true);
            ClickOncePayloadFileResolver payloadResolver = new();
            ClickOnceDeployManifestFileGraphResolver resolver = new(
                new ClickOnceManifestReader(),
                payloadResolver);

            ClickOnceFileGraph graph = resolver.Resolve(deploymentManifest);

            ClickOnceFileGraphEntry entry = Assert.Single(graph.Payloads);

            Assert.Equal(payload.FullName, entry.Source.FullName);
            Assert.Equal(PayloadFileName, entry.TargetPath);
            Assert.Equal(DeploySuffix, entry.MappingAddedSuffix);
            Assert.Collection(
                graph.Diagnostics,
                diagnostic =>
                {
                    Assert.Equal(OutputMessageType.Error, diagnostic.Type);
                    Assert.Contains(
                        PayloadFileName,
                        diagnostic.Text,
                        StringComparison.Ordinal);
                },
                diagnostic =>
                {
                    Assert.Equal(OutputMessageType.Error, diagnostic.Type);
                    Assert.Contains(
                        PayloadFileName,
                        diagnostic.Text,
                        StringComparison.Ordinal);
                });
        }

        private static IApplicationManifest CreateApplicationManifestSubstitute(
            ApplicationManifest model,
            ICollection<string[]> resolutionSearches)
        {
            IApplicationManifest manifest =
                Substitute.For<IApplicationManifest>();

            manifest.AssemblyReferences.Returns(model.AssemblyReferences);
            manifest.EntryPoint.Returns(model.EntryPoint);
            manifest.FileReferences.Returns(model.FileReferences);
            manifest.Diagnostics.Returns(
                Array.Empty<ClickOnceManifestDiagnostic>());
            manifest
                .When(value => value.ResolveFiles(
                    Arg.Any<IReadOnlyList<DirectoryInfo>>()))
                .Do(callInfo =>
                    resolutionSearches.Add(
                        ((IReadOnlyList<DirectoryInfo>)callInfo[0]!)
                            .Select(directory => directory.FullName)
                            .ToArray()));

            return manifest;
        }

        private static IDeployManifest CreateDeployManifestSubstitute(
            DeployManifest model,
            ICollection<string[]> resolutionSearches)
        {
            IDeployManifest manifest = Substitute.For<IDeployManifest>();

            manifest.AssemblyReferences.Returns(model.AssemblyReferences);
            manifest.EntryPoint.Returns(model.EntryPoint);
            manifest.FileReferences.Returns(model.FileReferences);
            manifest.Diagnostics.Returns(
                Array.Empty<ClickOnceManifestDiagnostic>());
            manifest
                .When(value => value.ResolveFiles(
                    Arg.Any<IReadOnlyList<DirectoryInfo>>()))
                .Do(callInfo =>
                    resolutionSearches.Add(
                        ((IReadOnlyList<DirectoryInfo>)callInfo[0]!)
                            .Select(directory => directory.FullName)
                            .ToArray()));

            return manifest;
        }

        private static IClickOnceManifestReader CreateManifestReader(
            IDeployManifest deploymentManifest,
            IApplicationManifest applicationManifest)
        {
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

            return manifestReader;
        }

        private static string GetCandidatePath(
            DirectoryInfo searchDirectory,
            string targetPath)
        {
            return new FileInfo(
                Path.Combine(
                    searchDirectory.FullName,
                    targetPath)).FullName;
        }

        private static void AssertSearchPaths(
            IEnumerable<string> actual,
            params string[] expected)
        {
            Assert.True(
                expected.SequenceEqual(
                    actual,
                    StringComparer.OrdinalIgnoreCase));
        }
    }
}
