// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Reflection;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using NSubstitute;

namespace Sign.Core.Test
{
    public sealed class ClickOnceFileGraphStagerTests : IDisposable
    {
        private const string ApplicationDirectory =
            @"Application Files\App_1_0_0_0";
        private const string ApplicationManifestFileName = "App.exe.manifest";
        private const string DeploySuffix = ".deploy";
        private const string DeploymentManifestFileName = "App.application";
        private const string FileContents = "contents";
        private const string LauncherFileName = "Launcher.exe";
        private const string PayloadFileName = "payload.dll";
        private const string SetupFileName = "setup.exe";
        private const string WarningMessageName =
            "GenerateManifest.ResolveFailedInReadWriteMode";
        private const string WarningTargetPath = "warning.txt";

        private readonly DirectoryServiceStub _directoryService;
        private readonly ClickOnceFileGraphStager _stager;

        public ClickOnceFileGraphStagerTests()
        {
            _directoryService = new DirectoryServiceStub();
            _stager = new ClickOnceFileGraphStager(_directoryService);
        }

        public void Dispose()
        {
            _directoryService.Dispose();
        }

        [Fact]
        public void Stage_DefaultDeployment_StagesGraphAndRebindsReferences()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo deploymentManifest = CreateFile(
                sourceDirectory,
                DeploymentManifestFileName);
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                Path.Combine(
                    ApplicationDirectory,
                    ApplicationManifestFileName));
            FileInfo payload = CreateFile(
                sourceDirectory,
                Path.Combine(ApplicationDirectory, PayloadFileName));
            FileInfo competingPayload = CreateFile(
                sourceDirectory,
                Path.Combine("competing", PayloadFileName));
            FileInfo setup = CreateFile(sourceDirectory, SetupFileName);
            AssemblyReference applicationReference = new()
            {
                ResolvedPath = applicationManifest.FullName,
                TargetPath = Path.Combine(
                    ApplicationDirectory,
                    ApplicationManifestFileName)
            };
            FileReference payloadReference = new()
            {
                ResolvedPath = competingPayload.FullName,
                SourcePath = competingPayload.FullName,
                TargetPath = PayloadFileName
            };
            ClickOnceFileGraph graph = CreateDeploymentGraph(
                deploymentManifest,
                applicationManifest,
                applicationReference,
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        payloadReference)
                },
                new[]
                {
                    CreateEntry(
                        setup,
                        SetupFileName,
                        ClickOnceFileGraphEntryKind.Setup)
                });

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);

            Assert.Equal(4, result.Files.Count);
            Assert.All(
                result.Files,
                file => Assert.False(file.IsUpdateInputOnly));
            AssertFile(
                result.Directory,
                DeploymentManifestFileName,
                FileContents);
            AssertFile(
                result.Directory,
                Path.Combine(
                    ApplicationDirectory,
                    ApplicationManifestFileName),
                FileContents);
            AssertFile(
                result.Directory,
                Path.Combine(ApplicationDirectory, PayloadFileName),
                FileContents);
            AssertFile(result.Directory, SetupFileName, FileContents);
            Assert.Equal(
                Path.Combine(
                    result.Directory.FullName,
                    ApplicationDirectory,
                    ApplicationManifestFileName),
                applicationReference.ResolvedPath);
            Assert.Equal(
                Path.Combine(
                    result.Directory.FullName,
                    ApplicationDirectory,
                    PayloadFileName),
                payloadReference.ResolvedPath);
        }

        [Fact]
        public void
            Stage_NoSignDependenciesForApplication_StagesReadOnlyInputs()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                PayloadFileName);
            FileReference payloadReference = new()
            {
                ResolvedPath = payload.FullName,
                TargetPath = PayloadFileName
            };
            IApplicationManifest applicationModel =
                Substitute.For<IApplicationManifest>();
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                applicationModel,
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        payloadReference)
                });

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.NoSignDependencies);

            Assert.Equal(2, result.Files.Count);
            Assert.False(
                Assert.Single(
                    result.Files,
                    file =>
                        file.Kind ==
                        ClickOnceFileGraphEntryKind.ApplicationManifest)
                .IsUpdateInputOnly);
            Assert.True(
                Assert.Single(
                    result.Files,
                    file =>
                        file.Kind ==
                        ClickOnceFileGraphEntryKind.Payload)
                .IsUpdateInputOnly);
            applicationModel.DidNotReceive().UpdateFileInfo();
            applicationModel.DidNotReceive().Write(
                Arg.Any<FileInfo>());
        }

        [Fact]
        public void
            Stage_NoSignDependenciesForDeployment_StagesOnlyRequiredInput()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo deploymentManifest = CreateFile(
                sourceDirectory,
                DeploymentManifestFileName);
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                PayloadFileName);
            FileInfo launcher = CreateFile(
                sourceDirectory,
                LauncherFileName);
            AssemblyReference applicationReference = new()
            {
                ResolvedPath = applicationManifest.FullName,
                TargetPath = ApplicationManifestFileName
            };
            ClickOnceFileGraph graph = CreateDeploymentGraph(
                deploymentManifest,
                applicationManifest,
                applicationReference,
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference())
                },
                new[]
                {
                    CreateEntry(
                        launcher,
                        LauncherFileName,
                        ClickOnceFileGraphEntryKind.Launcher)
                });

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.NoSignDependencies);

            Assert.Collection(
                result.Files.OrderBy(file => file.Kind),
                file =>
                {
                    Assert.Equal(
                        ClickOnceFileGraphEntryKind.DeploymentManifest,
                        file.Kind);
                    Assert.False(file.IsUpdateInputOnly);
                },
                file =>
                {
                    Assert.Equal(
                        ClickOnceFileGraphEntryKind.ApplicationManifest,
                        file.Kind);
                    Assert.True(file.IsUpdateInputOnly);
                });
            Assert.False(
                File.Exists(
                    Path.Combine(
                        result.Directory.FullName,
                        PayloadFileName)));
            Assert.False(
                File.Exists(
                    Path.Combine(
                        result.Directory.FullName,
                        LauncherFileName)));
        }

        [Fact]
        public void Stage_NoUpdate_StagesOnlyExplicitManifest()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo deploymentManifest = CreateFile(
                sourceDirectory,
                DeploymentManifestFileName);
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            AssemblyReference applicationReference = new()
            {
                ResolvedPath = applicationManifest.FullName,
                TargetPath = ApplicationManifestFileName
            };
            ClickOnceFileGraph graph = CreateDeploymentGraph(
                deploymentManifest,
                applicationManifest,
                applicationReference,
                new[]
                {
                    CreateEntry(
                        new FileInfo(
                            Path.Combine(
                                sourceDirectory.FullName,
                                "missing.dll")),
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference())
                });

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.NoUpdate);

            ClickOnceStagedFile stagedFile = Assert.Single(result.Files);

            Assert.Equal(
                ClickOnceFileGraphEntryKind.DeploymentManifest,
                stagedFile.Kind);
            Assert.Equal(
                applicationManifest.FullName,
                applicationReference.ResolvedPath);
        }

        [Fact]
        public void Stage_NoUpdateApplication_StagesOnlyExplicitManifest()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        new FileInfo(
                            Path.Combine(
                                sourceDirectory.FullName,
                                "missing.dll")),
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference())
                });

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.NoUpdate);

            ClickOnceStagedFile stagedFile = Assert.Single(result.Files);

            Assert.Equal(
                ClickOnceFileGraphEntryKind.ApplicationManifest,
                stagedFile.Kind);
        }

        [Theory]
        [InlineData(@"..\payload.dll")]
        [InlineData(@"sub\..\payload.dll")]
        [InlineData(@"sub\.\payload.dll")]
        [InlineData(@"\payload.dll")]
        [InlineData(@"C:\payload.dll")]
        [InlineData(@"C:payload.dll")]
        [InlineData(@"\\server\share\payload.dll")]
        [InlineData(@"\\?\C:\payload.dll")]
        [InlineData(@"\\.\NUL")]
        [InlineData("payload.dll:stream")]
        [InlineData("payload.dll.")]
        [InlineData("payload.dll ")]
        [InlineData(".")]
        public void Stage_UnsafeTargetPath_IsRemapped(string targetPath)
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                PayloadFileName);
            ClickOnceManifestDiagnostic diagnostic = CreateDiagnostic();
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        targetPath,
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference())
                },
                new[] { diagnostic });

            FileReference reference =
                (FileReference)Assert.Single(graph.Payloads)
                    .ManifestReference!;
            reference.TargetPath = targetPath;

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);

            ClickOnceStagedFile stagedFile = Assert.Single(
                result.Files,
                file => file.Kind == ClickOnceFileGraphEntryKind.Payload);
            string rootPath = Path.GetFullPath(result.Directory.FullName) +
                Path.DirectorySeparatorChar;
            string destinationPath = Path.GetFullPath(stagedFile.File.FullName);

            Assert.StartsWith(
                rootPath,
                destinationPath,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "__clickonce_internal_",
                destinationPath,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(targetPath, stagedFile.TargetPath);
            Assert.Equal(targetPath, reference.TargetPath);
            Assert.Equal(destinationPath, reference.ResolvedPath);
            Assert.Equal(FileContents, File.ReadAllText(destinationPath));
            Assert.Same(diagnostic, Assert.Single(result.Diagnostics));
            Assert.Equal(FileContents, File.ReadAllText(payload.FullName));
        }

        [Theory]
        [InlineData("CON")]
        [InlineData("CON.txt")]
        [InlineData("PRN")]
        [InlineData("AUX.dll")]
        [InlineData("NUL")]
        [InlineData("COM1")]
        [InlineData("COM1.dll")]
        [InlineData("COM\u00b9")]
        [InlineData("COM\u00b9.dll")]
        [InlineData("COM\u00b2")]
        [InlineData("COM\u00b2.dll")]
        [InlineData("COM\u00b3")]
        [InlineData("COM\u00b3.dll")]
        [InlineData("LPT\u00b9")]
        [InlineData("LPT\u00b9.dll")]
        [InlineData("LPT\u00b2")]
        [InlineData("LPT\u00b2.dll")]
        [InlineData("LPT\u00b3")]
        [InlineData("LPT\u00b3.dll")]
        public void Stage_LegacyWindowsDeviceName_UsesDotNetPathSemantics(
            string targetPath)
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                PayloadFileName);
            FileReference reference = new();
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        targetPath,
                        ClickOnceFileGraphEntryKind.Payload,
                        reference)
                });

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);

            ClickOnceStagedFile stagedFile = Assert.Single(
                result.Files,
                file => file.Kind == ClickOnceFileGraphEntryKind.Payload);

            Assert.Equal(targetPath, stagedFile.TargetPath);
            Assert.Equal(stagedFile.File.FullName, reference.ResolvedPath);
            Assert.Equal(
                FileContents,
                File.ReadAllText(stagedFile.File.FullName));
        }

        [Fact]
        public void Stage_UnsafeTargetPath_RemappingIsDeterministic()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                PayloadFileName);
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        @"..\payload.dll",
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference())
                });

            string firstRelativePath;

            using (ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default))
            {
                firstRelativePath = Path.GetRelativePath(
                    result.Directory.FullName,
                    Assert.Single(
                        result.Files,
                        file =>
                            file.Kind ==
                            ClickOnceFileGraphEntryKind.Payload)
                    .File.FullName);
            }

            ClickOnceFileGraph secondGraph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        @"C:\different.dll",
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference())
                });
            using ClickOnceStagingResult secondResult = _stager.Stage(
                secondGraph,
                ClickOnceSigningMode.Default);
            string secondRelativePath = Path.GetRelativePath(
                secondResult.Directory.FullName,
                Assert.Single(
                    secondResult.Files,
                    file =>
                        file.Kind ==
                        ClickOnceFileGraphEntryKind.Payload)
                .File.FullName);

            Assert.Equal(firstRelativePath, secondRelativePath);
        }

        [Fact]
        public void Stage_DuplicateUnsafeTarget_ReusesDestination()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                PayloadFileName);
            FileReference firstReference = new();
            FileReference secondReference = new();
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        @"..\payload.dll",
                        ClickOnceFileGraphEntryKind.Payload,
                        firstReference),
                    CreateEntry(
                        payload,
                        @"..\payload.dll",
                        ClickOnceFileGraphEntryKind.Payload,
                        secondReference)
                });

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);

            Assert.Equal(
                firstReference.ResolvedPath,
                secondReference.ResolvedPath);
            Assert.True(File.Exists(firstReference.ResolvedPath));
        }

        [Fact]
        public void Stage_DuplicateUnsafeTargetWithDifferentSources_Fails()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo firstPayload = CreateFile(
                sourceDirectory,
                Path.Combine("one", PayloadFileName));
            FileInfo secondPayload = CreateFile(
                sourceDirectory,
                Path.Combine("two", PayloadFileName));
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        firstPayload,
                        @"..\payload.dll",
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference()),
                    CreateEntry(
                        secondPayload,
                        @"..\payload.dll",
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference())
                });

            Assert.Throws<ClickOnceFileGraphStagingException>(
                () => _stager.Stage(
                    graph,
                    ClickOnceSigningMode.Default));
            Assert.False(_directoryService.Directories[^1].Exists);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Stage_GeneratedNamespace_IsIndependentOfCandidateOrder(
            bool unsafeFirst)
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo unsafePayload = CreateFile(
                sourceDirectory,
                Path.Combine("one", PayloadFileName));
            FileInfo directPayload = CreateFile(
                sourceDirectory,
                Path.Combine("two", PayloadFileName));
            string directTarget = Path.Combine(
                "__clickonce_internal_generated0",
                $"{(unsafeFirst ? 2 : 1):x8}",
                "file");
            ClickOnceFileGraphEntry unsafeEntry = CreateEntry(
                unsafePayload,
                @"..\payload.dll",
                ClickOnceFileGraphEntryKind.Payload,
                new FileReference());
            ClickOnceFileGraphEntry directEntry = CreateEntry(
                directPayload,
                directTarget,
                ClickOnceFileGraphEntryKind.Payload,
                new FileReference());
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                unsafeFirst
                    ? new[] { unsafeEntry, directEntry }
                    : new[] { directEntry, unsafeEntry });

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);
            ClickOnceStagedFile[] payloads = result.Files
                .Where(
                    file =>
                        file.Kind ==
                        ClickOnceFileGraphEntryKind.Payload)
                .ToArray();

            Assert.Equal(2, payloads.Length);
            ClickOnceStagedFile stagedDirect = Assert.Single(
                payloads,
                file => string.Equals(
                    file.Source.FullName,
                    directPayload.FullName,
                    StringComparison.OrdinalIgnoreCase));
            ClickOnceStagedFile stagedUnsafe = Assert.Single(
                payloads,
                file => string.Equals(
                    file.Source.FullName,
                    unsafePayload.FullName,
                    StringComparison.OrdinalIgnoreCase));

            Assert.NotEqual(
                stagedDirect.File.FullName,
                stagedUnsafe.File.FullName);
            Assert.NotEqual(
                Path.Combine(result.Directory.FullName, directTarget),
                stagedDirect.File.FullName);
            Assert.Equal(directTarget, stagedDirect.TargetPath);
            Assert.Equal(@"..\payload.dll", stagedUnsafe.TargetPath);
            Assert.All(payloads, file => Assert.True(file.File.Exists));
        }

        [Fact]
        public void Stage_CaseOnlyDestinationCollision_FailsBeforeCopying()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo firstPayload = CreateFile(
                sourceDirectory,
                Path.Combine("one", PayloadFileName));
            FileInfo secondPayload = CreateFile(
                sourceDirectory,
                Path.Combine("two", PayloadFileName));
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        firstPayload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference()),
                    CreateEntry(
                        secondPayload,
                        PayloadFileName.ToUpperInvariant(),
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference())
                });

            ClickOnceFileGraphStagingException exception =
                Assert.Throws<ClickOnceFileGraphStagingException>(
                    () => _stager.Stage(
                        graph,
                        ClickOnceSigningMode.Default));

            Assert.Contains(firstPayload.FullName, exception.Message);
            Assert.Contains(secondPayload.FullName, exception.Message);
            Assert.Contains(PayloadFileName, exception.Message);
            Assert.Contains(
                PayloadFileName.ToUpperInvariant(),
                exception.Message);
            Assert.DoesNotContain(
                "staging",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(_directoryService.Directories[^1].Exists);
        }

        [Fact]
        public void Stage_MappedUpdateDestinationCollision_FailsBeforeCopying()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo mappedPayload = CreateFile(
                sourceDirectory,
                $"{PayloadFileName}{DeploySuffix}");
            FileInfo unmappedPayload = CreateFile(
                sourceDirectory,
                Path.Combine("other", PayloadFileName));
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        mappedPayload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference(),
                        DeploySuffix),
                    CreateEntry(
                        unmappedPayload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference())
                });

            Assert.Throws<ClickOnceFileGraphStagingException>(
                () => _stager.Stage(
                    graph,
                    ClickOnceSigningMode.Default));
            Assert.False(_directoryService.Directories[^1].Exists);
        }

        [Fact]
        public void
            Stage_MixedMappedAndUnmappedSameDestination_FailsBeforeCopying()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                $"{PayloadFileName}{DeploySuffix}");
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference(),
                        DeploySuffix),
                    CreateEntry(
                        payload,
                        $"{PayloadFileName}{DeploySuffix}",
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference())
                });

            Assert.Throws<ClickOnceFileGraphStagingException>(
                () => _stager.Stage(
                    graph,
                    ClickOnceSigningMode.Default));
            Assert.False(_directoryService.Directories[^1].Exists);
        }

        [Fact]
        public void Stage_OneReferenceWithMultipleDestinations_FailsBeforeCopying()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                PayloadFileName);
            FileReference reference = new();
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        Path.Combine("one", PayloadFileName),
                        ClickOnceFileGraphEntryKind.Payload,
                        reference),
                    CreateEntry(
                        payload,
                        Path.Combine("two", PayloadFileName),
                        ClickOnceFileGraphEntryKind.Payload,
                        reference)
                });

            ClickOnceFileGraphStagingException exception =
                Assert.Throws<ClickOnceFileGraphStagingException>(
                    () => _stager.Stage(
                        graph,
                        ClickOnceSigningMode.Default));

            Assert.Contains(payload.FullName, exception.Message);
            Assert.Contains(
                Path.Combine("one", PayloadFileName),
                exception.Message);
            Assert.Contains(
                Path.Combine("two", PayloadFileName),
                exception.Message);
            Assert.DoesNotContain(
                "staging",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(_directoryService.Directories[^1].Exists);
        }

        [Fact]
        public void Stage_MappedTrailingDotTarget_IsRemapped()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            const string TargetPath = "payload.";
            FileInfo payload = CreateFile(
                sourceDirectory,
                $"{TargetPath}{DeploySuffix}");
            FileReference reference = new()
            {
                TargetPath = TargetPath
            };
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        TargetPath,
                        ClickOnceFileGraphEntryKind.Payload,
                        reference,
                        DeploySuffix)
                });

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);
            ClickOnceStagedFile stagedFile = Assert.Single(
                result.Files,
                file => file.Kind == ClickOnceFileGraphEntryKind.Payload);

            Assert.Equal(TargetPath, stagedFile.TargetPath);
            Assert.Equal(TargetPath, reference.TargetPath);
            Assert.NotNull(stagedFile.ManifestUpdateFile);
            Assert.Equal(
                $"{stagedFile.ManifestUpdateFile!.FullName}{DeploySuffix}",
                stagedFile.File.FullName);
            Assert.Equal(stagedFile.File.FullName, reference.ResolvedPath);

            using (result.BeginManifestUpdate())
            {
                Assert.False(stagedFile.File.Exists);
                stagedFile.ManifestUpdateFile!.Refresh();
                Assert.True(stagedFile.ManifestUpdateFile.Exists);
                Assert.Equal(
                    stagedFile.ManifestUpdateFile.FullName,
                    reference.ResolvedPath);
            }

            stagedFile.File.Refresh();
            stagedFile.ManifestUpdateFile!.Refresh();
            Assert.True(stagedFile.File.Exists);
            Assert.False(stagedFile.ManifestUpdateFile.Exists);
            Assert.Equal(stagedFile.File.FullName, reference.ResolvedPath);
        }

        [Fact]
        public void Stage_PathTooLong_IsRemappedAndPreservesDiagnostics()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                PayloadFileName);
            ClickOnceManifestDiagnostic diagnostic = CreateDiagnostic();
            string targetPath = string.Join(
                Path.DirectorySeparatorChar,
                Enumerable.Repeat("segment", 5000));
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        targetPath,
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference())
                },
                new[] { diagnostic });

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);
            ClickOnceStagedFile stagedFile = Assert.Single(
                result.Files,
                file => file.Kind == ClickOnceFileGraphEntryKind.Payload);

            Assert.Equal(targetPath, stagedFile.TargetPath);
            Assert.True(stagedFile.File.Exists);
            Assert.Same(diagnostic, Assert.Single(result.Diagnostics));
        }

        [Fact]
        public void Stage_FileDirectoryCollision_FailsBeforeCopying()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo firstPayload = CreateFile(
                sourceDirectory,
                Path.Combine("one", "payload"));
            FileInfo secondPayload = CreateFile(
                sourceDirectory,
                Path.Combine("two", PayloadFileName));
            FileInfo interveningPayload = CreateFile(
                sourceDirectory,
                Path.Combine("three", PayloadFileName));
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        firstPayload,
                        "directory",
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference()),
                    CreateEntry(
                        interveningPayload,
                        "directory0",
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference()),
                    CreateEntry(
                        secondPayload,
                        Path.Combine("directory", PayloadFileName),
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference())
                });

            Assert.Throws<ClickOnceFileGraphStagingException>(
                () => _stager.Stage(
                    graph,
                    ClickOnceSigningMode.Default));
            Assert.False(_directoryService.Directories[^1].Exists);
        }

        [Fact]
        public void
            BeginManifestUpdate_RemappedTargetEndingInSuffix_UsesExactSuffix()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            string targetPath = Path.Combine("..", "payload.deploy");

            FileInfo payload = CreateFile(
                sourceDirectory,
                $"payload.deploy{DeploySuffix}");
            FileReference payloadReference = new()
            {
                ResolvedPath = payload.FullName,
                TargetPath = targetPath
            };
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        targetPath,
                        ClickOnceFileGraphEntryKind.Payload,
                        payloadReference,
                        DeploySuffix)
                });

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);

            ClickOnceStagedFile stagedFile = Assert.Single(
                result.Files,
                file => file.Kind == ClickOnceFileGraphEntryKind.Payload);
            FileInfo updateFile = Assert.IsType<FileInfo>(
                stagedFile.ManifestUpdateFile);
            string stagedPath = stagedFile.File.FullName;
            string updatePath = updateFile.FullName;

            Assert.Equal(targetPath, stagedFile.TargetPath);
            Assert.Equal(targetPath, payloadReference.TargetPath);
            Assert.Equal($"{updatePath}{DeploySuffix}", stagedPath);
            Assert.Equal(
                $"{updateFile.Name}{DeploySuffix}",
                stagedFile.File.Name);
            Assert.True(File.Exists(stagedPath));
            Assert.Equal(stagedPath, payloadReference.ResolvedPath);

            using (result.BeginManifestUpdate())
            {
                Assert.False(File.Exists(stagedPath));
                Assert.True(File.Exists(updatePath));
                Assert.Equal(updatePath, payloadReference.ResolvedPath);
            }

            Assert.True(File.Exists(stagedPath));
            Assert.False(File.Exists(updatePath));
            Assert.Equal(stagedPath, payloadReference.ResolvedPath);
            Assert.True(payload.Exists);
            Assert.Equal(FileContents, File.ReadAllText(payload.FullName));
        }

        [Fact]
        public void BeginManifestUpdate_WhenRenameFails_RollsBackEarlierFiles()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo firstPayload = CreateFile(
                sourceDirectory,
                $"one.dll{DeploySuffix}");
            FileInfo secondPayload = CreateFile(
                sourceDirectory,
                $"two.dll{DeploySuffix}");
            FileReference firstReference = new();
            FileReference secondReference = new();
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        firstPayload,
                        "one.dll",
                        ClickOnceFileGraphEntryKind.Payload,
                        firstReference,
                        DeploySuffix),
                    CreateEntry(
                        secondPayload,
                        "two.dll",
                        ClickOnceFileGraphEntryKind.Payload,
                        secondReference,
                        DeploySuffix)
                });

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);

            string firstStagedPath = Path.Combine(
                result.Directory.FullName,
                $"one.dll{DeploySuffix}");
            string secondStagedPath = Path.Combine(
                result.Directory.FullName,
                $"two.dll{DeploySuffix}");
            string secondUpdatePath = Path.Combine(
                result.Directory.FullName,
                "two.dll");

            File.WriteAllText(secondUpdatePath, FileContents);

            ClickOnceFileGraphStagingException exception =
                Assert.Throws<ClickOnceFileGraphStagingException>(
                    result.BeginManifestUpdate);

            Assert.Contains("two.dll", exception.Message);
            Assert.IsType<IOException>(exception.InnerException);
            Assert.DoesNotContain(
                "staging",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(firstStagedPath));
            Assert.True(File.Exists(secondStagedPath));
            Assert.Equal(firstStagedPath, firstReference.ResolvedPath);
            Assert.Equal(secondStagedPath, secondReference.ResolvedPath);
        }

        [Fact]
        public void BeginManifestUpdate_WhenAlreadyActive_Throws()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                $"{PayloadFileName}{DeploySuffix}");
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference(),
                        DeploySuffix)
                });
            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);
            using IDisposable update = result.BeginManifestUpdate();

            Assert.Throws<InvalidOperationException>(
                result.BeginManifestUpdate);
        }

        [Fact]
        public void BeginManifestUpdate_WhenResultIsDisposed_Throws()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>());
            ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);

            result.Dispose();

            Assert.Throws<ObjectDisposedException>(
                result.BeginManifestUpdate);
        }

        [Fact]
        public void ManifestUpdateScope_WhenDisposedTwice_DoesNothing()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                $"{PayloadFileName}{DeploySuffix}");
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference(),
                        DeploySuffix)
                });
            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);
            IDisposable update = result.BeginManifestUpdate();

            update.Dispose();
            update.Dispose();
        }

        [Fact]
        public void ManifestUpdateScope_WhenOwnerIsDisposed_DoesNothing()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                $"{PayloadFileName}{DeploySuffix}");
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference(),
                        DeploySuffix)
                });
            ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);
            IDisposable update = result.BeginManifestUpdate();

            result.Dispose();
            update.Dispose();
        }

        [Fact]
        public void EndManifestUpdate_WhenMultipleRestoresFail_AttemptsAll()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo firstPayload = CreateFile(
                sourceDirectory,
                $"one.dll{DeploySuffix}");
            FileInfo secondPayload = CreateFile(
                sourceDirectory,
                $"two.dll{DeploySuffix}");
            FileReference firstReference = new();
            FileReference secondReference = new();
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        firstPayload,
                        "one.dll",
                        ClickOnceFileGraphEntryKind.Payload,
                        firstReference,
                        DeploySuffix),
                    CreateEntry(
                        secondPayload,
                        "two.dll",
                        ClickOnceFileGraphEntryKind.Payload,
                        secondReference,
                        DeploySuffix)
                });
            ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);
            IDisposable update = result.BeginManifestUpdate();
            ClickOnceStagedFile[] mappedFiles = result.Files
                .Where(file => file.ManifestUpdateFile is not null)
                .ToArray();

            foreach (ClickOnceStagedFile mappedFile in mappedFiles)
            {
                File.WriteAllText(mappedFile.File.FullName, FileContents);
            }

            ClickOnceFileGraphStagingException exception =
                Assert.Throws<ClickOnceFileGraphStagingException>(
                    update.Dispose);
            AggregateException aggregate =
                Assert.IsType<AggregateException>(exception.InnerException);

            Assert.Equal(2, aggregate.InnerExceptions.Count);
            Assert.All(
                aggregate.InnerExceptions,
                inner => Assert.IsType<ClickOnceFileGraphStagingException>(
                    inner));
            Assert.All(
                mappedFiles,
                mappedFile =>
                {
                    mappedFile.ManifestUpdateFile!.Refresh();
                    Assert.True(mappedFile.ManifestUpdateFile.Exists);
                    Assert.Equal(
                        mappedFile.ManifestUpdateFile.FullName,
                        mappedFile.ManifestReference!.ResolvedPath);
                    File.Delete(mappedFile.File.FullName);
                });

            result.Dispose();
        }

        [Fact]
        public void Dispose_WhenActiveUpdateCannotRestore_CleansUp()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                $"{PayloadFileName}{DeploySuffix}");
            FileReference reference = new()
            {
                ResolvedPath = payload.FullName
            };
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        reference,
                        DeploySuffix)
                });
            ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);
            DirectoryInfo stagingDirectory = result.Directory;
            result.BeginManifestUpdate();
            string stablePath = Path.Combine(
                stagingDirectory.FullName,
                $"{PayloadFileName}{DeploySuffix}");
            File.WriteAllText(stablePath, FileContents);

            Assert.Throws<ClickOnceFileGraphStagingException>(
                result.Dispose);

            stagingDirectory.Refresh();
            Assert.False(stagingDirectory.Exists);
            Assert.Equal(payload.FullName, reference.ResolvedPath);
        }

        [Fact]
        public void Dispose_RestoresReferencesAndDeletesStagingDirectory()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                PayloadFileName);
            FileReference payloadReference = new()
            {
                ResolvedPath = payload.FullName,
                TargetPath = PayloadFileName
            };
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        payloadReference)
                });
            ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);
            DirectoryInfo stagingDirectory = result.Directory;

            Assert.NotEqual(
                payload.FullName,
                payloadReference.ResolvedPath);

            result.Dispose();
            stagingDirectory.Refresh();

            Assert.Equal(payload.FullName, payloadReference.ResolvedPath);
            Assert.False(stagingDirectory.Exists);
        }

        [Fact]
        public void Stage_CopyFailure_CleansUpAndPreservesDiagnostics()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo missingPayload = new(
                Path.Combine(sourceDirectory.FullName, PayloadFileName));
            ClickOnceManifestDiagnostic diagnostic = CreateDiagnostic();
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        missingPayload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        new FileReference())
                },
                new[] { diagnostic });

            ClickOnceFileGraphStagingException exception =
                Assert.Throws<ClickOnceFileGraphStagingException>(
                    () => _stager.Stage(
                        graph,
                        ClickOnceSigningMode.Default));

            Assert.Contains(missingPayload.FullName, exception.Message);
            Assert.DoesNotContain(
                "staging",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Same(diagnostic, Assert.Single(exception.Diagnostics));
            Assert.False(_directoryService.Directories[^1].Exists);
        }

        [Fact]
        public void Stage_DuplicateSameSourceAndDestination_BindsAllReferences()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                PayloadFileName);
            FileReference firstReference = new();
            FileReference secondReference = new();
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        firstReference),
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        ClickOnceFileGraphEntryKind.Payload,
                        secondReference)
                });

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);

            string stagedPath = Path.Combine(
                result.Directory.FullName,
                PayloadFileName);

            Assert.Equal(stagedPath, firstReference.ResolvedPath);
            Assert.Equal(stagedPath, secondReference.ResolvedPath);
            Assert.Equal(FileContents, File.ReadAllText(stagedPath));
        }

        [Fact]
        public void
            Stage_SeparatorEquivalentSameSourceAndDestination_BindsAllReferences()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                PayloadFileName);
            FileReference firstReference = new();
            FileReference secondReference = new();
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        $"sub/{PayloadFileName}",
                        ClickOnceFileGraphEntryKind.Payload,
                        firstReference),
                    CreateEntry(
                        payload,
                        $@"sub\{PayloadFileName}",
                        ClickOnceFileGraphEntryKind.Payload,
                        secondReference)
                });

            using ClickOnceStagingResult result = _stager.Stage(
                graph,
                ClickOnceSigningMode.Default);

            Assert.Equal(
                firstReference.ResolvedPath,
                secondReference.ResolvedPath);
            Assert.Equal(
                FileContents,
                File.ReadAllText(firstReference.ResolvedPath));
        }

        [Fact]
        public void Stage_InvalidMode_Throws()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            ClickOnceFileGraph graph = CreateApplicationGraph(
                applicationManifest,
                Substitute.For<IApplicationManifest>());

            Assert.Throws<ArgumentOutOfRangeException>(
                () => _stager.Stage(
                    graph,
                    (ClickOnceSigningMode)int.MaxValue));
        }

        private static ClickOnceFileGraph CreateApplicationGraph(
            FileInfo applicationManifest,
            IApplicationManifest applicationModel,
            IEnumerable<ClickOnceFileGraphEntry>? payloads = null,
            IEnumerable<ClickOnceManifestDiagnostic>? diagnostics = null)
        {
            return new ClickOnceFileGraph(
                deploymentManifest: null,
                deployManifest: null,
                CreateEntry(
                    applicationManifest,
                    applicationManifest.Name,
                    ClickOnceFileGraphEntryKind.ApplicationManifest),
                applicationModel,
                payloads ?? Array.Empty<ClickOnceFileGraphEntry>(),
                adjacentExecutables: Array.Empty<ClickOnceFileGraphEntry>(),
                diagnostics ?? Array.Empty<ClickOnceManifestDiagnostic>());
        }

        private static ClickOnceFileGraph CreateDeploymentGraph(
            FileInfo deploymentManifest,
            FileInfo applicationManifest,
            AssemblyReference applicationReference,
            IEnumerable<ClickOnceFileGraphEntry>? payloads = null,
            IEnumerable<ClickOnceFileGraphEntry>? adjacentExecutables = null)
        {
            return new ClickOnceFileGraph(
                CreateEntry(
                    deploymentManifest,
                    deploymentManifest.Name,
                    ClickOnceFileGraphEntryKind.DeploymentManifest),
                Substitute.For<IDeployManifest>(),
                CreateEntry(
                    applicationManifest,
                    applicationReference.TargetPath,
                    ClickOnceFileGraphEntryKind.ApplicationManifest,
                    applicationReference),
                Substitute.For<IApplicationManifest>(),
                payloads ?? Array.Empty<ClickOnceFileGraphEntry>(),
                adjacentExecutables ?? Array.Empty<ClickOnceFileGraphEntry>(),
                diagnostics: Array.Empty<ClickOnceManifestDiagnostic>());
        }

        private static ClickOnceFileGraphEntry CreateEntry(
            FileInfo source,
            string targetPath,
            ClickOnceFileGraphEntryKind kind,
            BaseReference? reference = null,
            string? mappingAddedSuffix = null)
        {
            return new ClickOnceFileGraphEntry(
                source,
                targetPath,
                kind,
                reference,
                mappingAddedSuffix);
        }

        private static FileInfo CreateFile(
            DirectoryInfo root,
            string relativePath)
        {
            FileInfo file = new(
                Path.Combine(root.FullName, relativePath));

            file.Directory!.Create();
            File.WriteAllText(file.FullName, FileContents);
            file.Refresh();

            return file;
        }

        private static void AssertFile(
            DirectoryInfo root,
            string relativePath,
            string expectedContents)
        {
            string path = Path.Combine(root.FullName, relativePath);

            Assert.True(File.Exists(path));
            Assert.Equal(expectedContents, File.ReadAllText(path));
        }

        private static ClickOnceManifestDiagnostic CreateDiagnostic()
        {
            DeployManifest manifest = new();
            MethodInfo addWarning = typeof(OutputMessageCollection).GetMethod(
                name: "AddWarningMessage",
                bindingAttr:
                    BindingFlags.Instance |
                    BindingFlags.NonPublic)!;

            addWarning.Invoke(
                obj: manifest.OutputMessages,
                parameters: new object[]
                {
                    WarningMessageName,
                    new[] { WarningTargetPath }
                });

            return new ClickOnceManifestDiagnostic(
                Assert.Single(
                    manifest.OutputMessages.Cast<OutputMessage>()));
        }
    }
}
