// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
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

        public static TheoryData<string> RelativeTargetPaths
        {
            get
            {
                return new TheoryData<string>()
                {
                    @"content\nested.dll",
                    @"..\parent.dll"
                };
            }
        }

        public static TheoryData<string> RootedTargetPaths
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
                    $@"\\?\{driveRoot}published\device.dll"
                };
            }
        }

        public void Dispose()
        {
            _directoryService.Dispose();
        }

        [Theory]
        [MemberData(nameof(RelativeTargetPaths))]
        public void ExplicitApplicationPublishLayoutResolver_WithRelativeTargetPath_PreservesTargetPathAndWindowsCandidate(
            string targetPath)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo applicationManifestFile =
                ClickOnceResolutionTestUtilities.CreateFile(
                    root,
                    ApplicationManifestFileName);
            ApplicationManifest applicationModel =
                ClickOnceResolutionTestUtilities.CreateApplicationManifest();
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
            ClickOncePayloadResolver payloadResolver = new(
                file =>
                {
                    probeCandidates.Add(file.FullName);

                    return true;
                });
            ClickOnceApplicationPublishLayoutResolver resolver = new(
                manifestReader,
                payloadResolver);

            Assert.True(
                resolver.TryResolve(
                    applicationManifestFile,
                    out ResolvedClickOncePublishLayout? layout));

            Assert.NotNull(layout);
            ResolvedClickOncePayload payload = Assert.Single(layout.Application.Payloads);
            string expectedCandidate = GetCandidatePath(root, targetPath);

            Assert.Equal(targetPath, payload.TargetPath);
            Assert.Equal(expectedCandidate, payload.Source.FullName);
            Assert.Same(reference, payload.Reference);
            Assert.Equal(
                expectedCandidate,
                Assert.Single(probeCandidates));
            AssertSearchPaths(
                Assert.Single(resolutionSearches),
                root.FullName);
        }

        [Theory]
        [MemberData(nameof(RelativeTargetPaths))]
        public void DeploymentPublishLayoutResolver_WithRelativeTargetPath_PreservesTargetPathAndWindowsCandidate(
            string targetPath)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo deploymentManifestFile =
                ClickOnceResolutionTestUtilities.CreateFile(
                    root,
                    DeploymentManifestFileName);
            FileInfo applicationManifestFile =
                ClickOnceResolutionTestUtilities.CreateFile(
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
                ClickOnceResolutionTestUtilities.CreateApplicationManifest();
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
            ClickOncePayloadResolver payloadResolver = new(
                file =>
                {
                    probeCandidates.Add(file.FullName);

                    return true;
                });
            ClickOnceDeploymentPublishLayoutResolver resolver = new(
                manifestReader,
                payloadResolver);

            ResolvedClickOncePublishLayout layout =
                resolver.Resolve(deploymentManifestFile);

            ResolvedClickOncePayload payload = Assert.Single(layout.Application.Payloads);
            string expectedCandidate = GetCandidatePath(
                applicationManifestFile.Directory!,
                targetPath);

            Assert.Equal(targetPath, payload.TargetPath);
            Assert.Equal(expectedCandidate, payload.Source.FullName);
            Assert.Same(reference, payload.Reference);
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

        [Theory]
        [MemberData(nameof(RootedTargetPaths))]
        public void ExplicitApplicationPublishLayoutResolver_WithRootedPayloadTargetPath_RejectsBeforeResolution(
            string targetPath)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo applicationManifestFile =
                ClickOnceResolutionTestUtilities.CreateFile(
                    root,
                    ApplicationManifestFileName);
            ApplicationManifest applicationModel =
                ClickOnceResolutionTestUtilities.CreateApplicationManifest();

            applicationModel.FileReferences.Add(
                new FileReference()
                {
                    TargetPath = targetPath
                });

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
            ClickOnceApplicationPublishLayoutResolver resolver = new(
                manifestReader,
                new ClickOncePayloadResolver(
                    file =>
                    {
                        probeCandidates.Add(file.FullName);
                        return true;
                    }));

            ClickOncePublishLayoutResolutionException exception =
                Assert.Throws<ClickOncePublishLayoutResolutionException>(
                    () => resolver.TryResolve(
                        applicationManifestFile,
                        out _));

            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceApplicationManifestInvalidTargetPath,
                    applicationManifestFile.FullName,
                    targetPath),
                exception.Message);
            Assert.Empty(resolutionSearches);
            Assert.Empty(probeCandidates);
        }

        [Theory]
        [MemberData(nameof(RootedTargetPaths))]
        public void DeploymentPublishLayoutResolver_WithRootedPayloadTargetPath_RejectsBeforeApplicationResolution(
            string targetPath)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo deploymentManifestFile =
                ClickOnceResolutionTestUtilities.CreateFile(
                    root,
                    DeploymentManifestFileName);
            FileInfo applicationManifestFile =
                ClickOnceResolutionTestUtilities.CreateFile(
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
            ClickOnceManifestDiagnostic expectedDiagnostic = new(
                "deployment-warning",
                "deployment warning",
                OutputMessageType.Warning);

            deploymentManifest.Diagnostics.Returns(
                new[] { expectedDiagnostic });
            ApplicationManifest applicationModel =
                ClickOnceResolutionTestUtilities.CreateApplicationManifest();

            applicationModel.FileReferences.Add(
                new FileReference()
                {
                    TargetPath = targetPath
                });

            List<string[]> applicationResolutionSearches = new();
            IApplicationManifest applicationManifest =
                CreateApplicationManifestSubstitute(
                    applicationModel,
                    applicationResolutionSearches);
            List<string> probeCandidates = new();
            ClickOnceDeploymentPublishLayoutResolver resolver = new(
                CreateManifestReader(
                    deploymentManifest,
                    applicationManifest),
                new ClickOncePayloadResolver(
                    file =>
                    {
                        probeCandidates.Add(file.FullName);
                        return true;
                    }));

            ClickOncePublishLayoutResolutionException exception =
                Assert.Throws<ClickOncePublishLayoutResolutionException>(
                    () => resolver.Resolve(deploymentManifestFile));

            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceApplicationManifestInvalidTargetPath,
                    applicationManifestFile.FullName,
                    targetPath),
                exception.Message);
            Assert.Single(deploymentResolutionSearches);
            Assert.Empty(applicationResolutionSearches);
            Assert.Empty(probeCandidates);
            Assert.Same(expectedDiagnostic, Assert.Single(exception.Diagnostics));
        }

        [Theory]
        [MemberData(nameof(RootedTargetPaths))]
        public void DeploymentPublishLayoutResolver_WithRootedEntryPointTargetPath_RejectsBeforeResolution(
            string targetPath)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo deploymentManifestFile =
                ClickOnceResolutionTestUtilities.CreateFile(
                    root,
                    DeploymentManifestFileName);
            AssemblyReference entryPoint = new()
            {
                TargetPath = targetPath
            };
            DeployManifest deploymentModel = new();

            deploymentModel.AssemblyReferences.Add(entryPoint);
            deploymentModel.EntryPoint = entryPoint;

            List<string[]> resolutionSearches = new();
            IDeployManifest deploymentManifest =
                CreateDeployManifestSubstitute(
                    deploymentModel,
                    resolutionSearches);
            ClickOnceManifestDiagnostic expectedDiagnostic = new(
                "deployment-warning",
                "deployment warning",
                OutputMessageType.Warning);

            deploymentManifest.Diagnostics.Returns(
                new[] { expectedDiagnostic });
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

            List<string> probeCandidates = new();
            ClickOnceDeploymentPublishLayoutResolver resolver = new(
                manifestReader,
                new ClickOncePayloadResolver(
                    file =>
                    {
                        probeCandidates.Add(file.FullName);
                        return true;
                    }));

            ClickOncePublishLayoutResolutionException exception =
                Assert.Throws<ClickOncePublishLayoutResolutionException>(
                    () => resolver.Resolve(deploymentManifestFile));

            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceDeploymentManifestInvalidTargetPath,
                    deploymentManifestFile.FullName,
                    targetPath),
                exception.Message);
            Assert.Empty(resolutionSearches);
            Assert.Empty(probeCandidates);
            Assert.Same(expectedDiagnostic, Assert.Single(exception.Diagnostics));
        }

        [Fact]
        public void DeploymentPublishLayoutResolver_WhenOnlyMappedPayloadExistsInDeploymentDirectory_UsesFallbackAndPreservesFirstAttemptDiagnostic()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo payload =
                ClickOnceResolutionTestUtilities.CreateFile(
                    root,
                    $"{PayloadFileName}{DeploySuffix}");
            FileInfo applicationManifest =
                ClickOnceResolutionTestUtilities.WriteApplicationManifest(
                    root,
                    $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                    PayloadFileName);
            FileInfo deploymentManifest =
                ClickOnceResolutionTestUtilities.WriteDeploymentManifest(
                    root,
                    DeploymentManifestFileName,
                    Path.GetRelativePath(
                        root.FullName,
                        applicationManifest.FullName),
                    mapFileExtensions: true);
            ClickOncePayloadResolver payloadResolver = new();
            ClickOnceDeploymentPublishLayoutResolver resolver = new(
                new ClickOnceManifestReader(),
                payloadResolver);

            ResolvedClickOncePublishLayout layout = resolver.Resolve(deploymentManifest);

            ResolvedClickOncePayload entry = Assert.Single(layout.Application.Payloads);

            Assert.Equal(payload.FullName, entry.Source.FullName);
            Assert.Equal(PayloadFileName, entry.TargetPath);
            Assert.True(entry.IsFileExtensionMapped);
            Assert.Collection(
                layout.Diagnostics,
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
