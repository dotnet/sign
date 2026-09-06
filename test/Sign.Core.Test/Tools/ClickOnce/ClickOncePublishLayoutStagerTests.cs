// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Reflection;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using NSubstitute;

namespace Sign.Core.Test
{
    public sealed class ClickOncePublishLayoutStagerTests : IDisposable
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
        private readonly ClickOncePublishLayoutStager _layoutStager;

        public ClickOncePublishLayoutStagerTests()
        {
            _directoryService = new DirectoryServiceStub();
            _layoutStager =
                new ClickOncePublishLayoutStager(_directoryService);
        }

        public void Dispose()
        {
            _directoryService.Dispose();
        }

        [Fact]
        public void Stage_Deployment_StagesLayoutAndRebindsReferences()
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
            ResolvedClickOncePublishLayout layout = CreateDeploymentLayout(
                deploymentManifest,
                applicationManifest,
                applicationReference,
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        payloadReference)
                },
                new[]
                {
                    CreateEntry(
                        setup,
                        SetupFileName,
                        TestPublishLayoutEntryKind.Setup)
                });

            using ClickOnceStagingSession session =
                _layoutStager.Stage(layout);

            Assert.NotNull(session.DeploymentManifest);
            Assert.True(
                PathsEqual(
                    session.DeploymentManifest.Source,
                    deploymentManifest));
            Assert.True(
                PathsEqual(
                    session.ApplicationManifest.Source,
                    applicationManifest));
            Assert.True(
                PathsEqual(
                    Assert.Single(session.Payloads).Value.Source,
                    payload));
            Assert.True(
                PathsEqual(
                    Assert.Single(session.AdjacentExecutables).Value.Source,
                    setup));
            AssertFile(
                session.Directory,
                DeploymentManifestFileName,
                FileContents);
            AssertFile(
                session.Directory,
                Path.Combine(
                    ApplicationDirectory,
                    ApplicationManifestFileName),
                FileContents);
            AssertFile(
                session.Directory,
                Path.Combine(ApplicationDirectory, PayloadFileName),
                FileContents);
            AssertFile(session.Directory, SetupFileName, FileContents);
            Assert.Equal(
                Path.Combine(
                    session.Directory.FullName,
                    ApplicationDirectory,
                    ApplicationManifestFileName),
                applicationReference.ResolvedPath);
            Assert.Equal(
                Path.Combine(
                    session.Directory.FullName,
                    ApplicationDirectory,
                    PayloadFileName),
                payloadReference.ResolvedPath);
        }

        [Fact]
        public void Stage_Application_StagesManifestAndPayloads()
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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        payloadReference)
                });

            using ClickOnceStagingSession session =
                _layoutStager.Stage(layout);

            Assert.Null(session.DeploymentManifest);
            Assert.True(
                PathsEqual(
                    session.ApplicationManifest.Source,
                    applicationManifest));
            Assert.True(
                PathsEqual(
                    Assert.Single(session.Payloads).Value.Source,
                    payload));
            Assert.Empty(session.AdjacentExecutables);
            Assert.Equal(
                Path.Combine(session.Directory.FullName, PayloadFileName),
                payloadReference.ResolvedPath);
        }

        [Fact]
        public void Build_Deployment_CreatesTypedPlanWithoutSideEffects()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            DirectoryInfo stagingDirectory =
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
                ResolvedPath = payload.FullName,
                TargetPath = PayloadFileName
            };
            ResolvedClickOncePublishLayout layout = CreateDeploymentLayout(
                deploymentManifest,
                applicationManifest,
                applicationReference,
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        payloadReference)
                },
                new[]
                {
                    CreateEntry(
                        setup,
                        SetupFileName,
                        TestPublishLayoutEntryKind.Setup)
                });
            ResolvedClickOncePayload resolvedPayload =
                Assert.Single(layout.Application.Payloads);
            ResolvedClickOnceAdjacentExecutable adjacentExecutable =
                Assert.Single(layout.AdjacentExecutables);
            ClickOnceStagingPlanBuilder builder = new();

            ClickOnceStagingPlan plan = builder.Build(
                layout,
                stagingDirectory);

            Assert.NotNull(plan.DeploymentManifest);
            Assert.True(
                PathsEqual(
                    plan.DeploymentManifest.Source,
                    deploymentManifest));
            Assert.True(
                PathsEqual(
                    plan.ApplicationManifest.Source,
                    applicationManifest));
            Assert.True(
                PathsEqual(
                    Assert.Single(plan.Payloads).Value.Source,
                    payload));
            Assert.True(
                PathsEqual(
                    Assert.Single(plan.AdjacentExecutables).Value.Source,
                    setup));
            Assert.Same(
                plan.Payloads[resolvedPayload],
                Assert.Single(plan.Payloads).Value);
            Assert.Same(
                plan.AdjacentExecutables[adjacentExecutable],
                Assert.Single(plan.AdjacentExecutables).Value);
            Assert.Equal(
                Path.Combine(
                    stagingDirectory.FullName,
                    ApplicationDirectory,
                    ApplicationManifestFileName),
                plan.ApplicationManifest.Destination.FullName);
            Assert.Equal(
                Path.Combine(
                    stagingDirectory.FullName,
                    ApplicationDirectory,
                    PayloadFileName),
                plan.Payloads[resolvedPayload].Destination.FullName);
            Assert.Equal(
                applicationManifest.FullName,
                applicationReference.ResolvedPath);
            Assert.Equal(payload.FullName, payloadReference.ResolvedPath);
            Assert.Empty(
                Directory.EnumerateFileSystemEntries(
                    stagingDirectory.FullName));
        }

        [Fact]
        public void Build_UnsafeTargetPath_FailsWithoutSideEffects()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            DirectoryInfo stagingDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                PayloadFileName);
            FileReference reference = new()
            {
                ResolvedPath = payload.FullName
            };
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        @"..\payload.dll",
                        TestPublishLayoutEntryKind.Payload,
                        reference)
                });
            ClickOnceStagingPlanBuilder builder = new();

            Assert.Throws<ClickOnceStagingException>(
                () => builder.Build(layout, stagingDirectory));

            Assert.Equal(payload.FullName, reference.ResolvedPath);
            Assert.Equal(FileContents, File.ReadAllText(payload.FullName));
            Assert.Empty(
                Directory.EnumerateFileSystemEntries(
                    stagingDirectory.FullName));
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
        public void Stage_UnsafeTargetPath_FailsBeforeCopying(string targetPath)
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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        targetPath,
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference())
                },
                new[] { diagnostic });

            ClickOnceStagingException exception =
                Assert.Throws<ClickOnceStagingException>(
                    () => _layoutStager.Stage(layout));

            Assert.Same(diagnostic, Assert.Single(exception.Diagnostics));
            Assert.False(_directoryService.Directories[^1].Exists);
            Assert.Equal(FileContents, File.ReadAllText(payload.FullName));
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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        firstPayload,
                        @"..\payload.dll",
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference()),
                    CreateEntry(
                        secondPayload,
                        @"..\payload.dll",
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference())
                });

            Assert.Throws<ClickOnceStagingException>(
                () => _layoutStager.Stage(layout));
            Assert.False(_directoryService.Directories[^1].Exists);
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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        firstPayload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference()),
                    CreateEntry(
                        secondPayload,
                        PayloadFileName.ToUpperInvariant(),
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference())
                });

            ClickOnceStagingException exception =
                Assert.Throws<ClickOnceStagingException>(
                    () => _layoutStager.Stage(layout));

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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        mappedPayload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference(),
                        DeploySuffix),
                    CreateEntry(
                        unmappedPayload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference())
                });

            Assert.Throws<ClickOnceStagingException>(
                () => _layoutStager.Stage(layout));
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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference(),
                        DeploySuffix),
                    CreateEntry(
                        payload,
                        $"{PayloadFileName}{DeploySuffix}",
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference())
                });

            Assert.Throws<ClickOnceStagingException>(
                () => _layoutStager.Stage(layout));
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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        Path.Combine("one", PayloadFileName),
                        TestPublishLayoutEntryKind.Payload,
                        reference),
                    CreateEntry(
                        payload,
                        Path.Combine("two", PayloadFileName),
                        TestPublishLayoutEntryKind.Payload,
                        reference)
                });

            ClickOnceStagingException exception =
                Assert.Throws<ClickOnceStagingException>(
                    () => _layoutStager.Stage(layout));

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
        public void Stage_MappedTrailingDotTarget_FailsBeforeCopying()
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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        TargetPath,
                        TestPublishLayoutEntryKind.Payload,
                        reference,
                        DeploySuffix)
                });

            Assert.Throws<ClickOnceStagingException>(
                () => _layoutStager.Stage(layout));
            Assert.False(_directoryService.Directories[^1].Exists);
        }

        [Fact]
        public void Stage_PathTooLong_FailsAndPreservesDiagnostics()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                "segment");
            ClickOnceManifestDiagnostic diagnostic = CreateDiagnostic();
            string targetPath = string.Join(
                Path.DirectorySeparatorChar,
                Enumerable.Repeat("segment", 5000));
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        targetPath,
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference())
                },
                new[] { diagnostic });

            ClickOnceStagingException exception =
                Assert.Throws<ClickOnceStagingException>(
                    () => _layoutStager.Stage(layout));

            Assert.Same(diagnostic, Assert.Single(exception.Diagnostics));
            Assert.False(_directoryService.Directories[^1].Exists);
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
                Path.Combine("one", "directory"));
            FileInfo secondPayload = CreateFile(
                sourceDirectory,
                Path.Combine("two", PayloadFileName));
            FileInfo interveningPayload = CreateFile(
                sourceDirectory,
                Path.Combine("three", "directory0"));
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        firstPayload,
                        "directory",
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference()),
                    CreateEntry(
                        interveningPayload,
                        "directory0",
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference()),
                    CreateEntry(
                        secondPayload,
                        Path.Combine("directory", PayloadFileName),
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference())
                });

            Assert.Throws<ClickOnceStagingException>(
                () => _layoutStager.Stage(layout));
            Assert.False(_directoryService.Directories[^1].Exists);
        }

        [Fact]
        public void
            Stage_MappedUnsafeTargetEndingInSuffix_FailsBeforeCopying()
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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        targetPath,
                        TestPublishLayoutEntryKind.Payload,
                        payloadReference,
                        DeploySuffix)
                });

            Assert.Throws<ClickOnceStagingException>(
                () => _layoutStager.Stage(layout));
            Assert.False(_directoryService.Directories[^1].Exists);
            Assert.Equal(FileContents, File.ReadAllText(payload.FullName));
        }

        [Fact]
        public void
            BeginManifestFileInfoUpdate_MappedTargetEndingInDeploy_UpdatesAndRestoresStagedFile()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            const string TargetPath = "payload.deploy";
            FileInfo payload = CreateFile(
                sourceDirectory,
                $"{TargetPath}{DeploySuffix}");
            FileReference reference = new()
            {
                ResolvedPath = payload.FullName,
                TargetPath = TargetPath
            };
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        TargetPath,
                        TestPublishLayoutEntryKind.Payload,
                        reference,
                        DeploySuffix)
                });
            ResolvedClickOncePayload resolvedPayload =
                Assert.Single(layout.Application.Payloads);

            using ClickOnceStagingSession session =
                _layoutStager.Stage(layout);

            ClickOnceStagedFile stagedPayload =
                session.Payloads[resolvedPayload];
            FileInfo fileInfoUpdateFile = Assert.IsType<FileInfo>(
                stagedPayload.FileInfoUpdateFile);
            string stablePath = Path.Combine(
                session.Directory.FullName,
                $"{TargetPath}{DeploySuffix}");
            string updatePath = Path.Combine(
                session.Directory.FullName,
                TargetPath);

            Assert.Equal(stablePath, stagedPayload.Destination.FullName);
            Assert.Equal(updatePath, fileInfoUpdateFile.FullName);
            Assert.True(File.Exists(stablePath));
            Assert.False(File.Exists(updatePath));
            Assert.Equal(stablePath, reference.ResolvedPath);

            using (session.BeginManifestFileInfoUpdate())
            {
                Assert.False(File.Exists(stablePath));
                Assert.True(File.Exists(updatePath));
                Assert.Equal(updatePath, reference.ResolvedPath);
                Assert.True(payload.Exists);
                Assert.Equal(FileContents, File.ReadAllText(payload.FullName));
            }

            Assert.True(File.Exists(stablePath));
            Assert.False(File.Exists(updatePath));
            Assert.Equal(stablePath, reference.ResolvedPath);
            Assert.True(payload.Exists);
            Assert.Equal(FileContents, File.ReadAllText(payload.FullName));
        }

        [Fact]
        public void BeginManifestFileInfoUpdate_WhenRenameFails_RollsBackEarlierFiles()
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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        firstPayload,
                        "one.dll",
                        TestPublishLayoutEntryKind.Payload,
                        firstReference,
                        DeploySuffix),
                    CreateEntry(
                        secondPayload,
                        "two.dll",
                        TestPublishLayoutEntryKind.Payload,
                        secondReference,
                        DeploySuffix)
                });

            using ClickOnceStagingSession session = _layoutStager.Stage(layout);

            string firstStagedPath = Path.Combine(
                session.Directory.FullName,
                $"one.dll{DeploySuffix}");
            string secondStagedPath = Path.Combine(
                session.Directory.FullName,
                $"two.dll{DeploySuffix}");
            string secondUpdatePath = Path.Combine(
                session.Directory.FullName,
                "two.dll");

            File.WriteAllText(secondUpdatePath, FileContents);

            ClickOnceStagingException exception =
                Assert.Throws<ClickOnceStagingException>(
                    session.BeginManifestFileInfoUpdate);

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
        public void BeginManifestFileInfoUpdate_WhenAlreadyActive_Throws()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                $"{PayloadFileName}{DeploySuffix}");
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference(),
                        DeploySuffix)
                });
            using ClickOnceStagingSession session = _layoutStager.Stage(layout);
            using IDisposable update = session.BeginManifestFileInfoUpdate();

            Assert.Throws<InvalidOperationException>(
                session.BeginManifestFileInfoUpdate);
        }

        [Fact]
        public void BeginManifestFileInfoUpdate_WhenSessionIsDisposed_Throws()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>());
            ClickOnceStagingSession session = _layoutStager.Stage(layout);

            session.Dispose();

            Assert.Throws<ObjectDisposedException>(
                session.BeginManifestFileInfoUpdate);
        }

        [Fact]
        public void ManifestFileInfoUpdateScope_WhenDisposedTwice_DoesNothing()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                $"{PayloadFileName}{DeploySuffix}");
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference(),
                        DeploySuffix)
                });
            using ClickOnceStagingSession session = _layoutStager.Stage(layout);
            IDisposable update = session.BeginManifestFileInfoUpdate();

            update.Dispose();
            update.Dispose();
        }

        [Fact]
        public void ManifestFileInfoUpdateScope_WhenOwnerIsDisposed_DoesNothing()
        {
            DirectoryInfo sourceDirectory =
                _directoryService.CreateTemporaryDirectory();
            FileInfo applicationManifest = CreateFile(
                sourceDirectory,
                ApplicationManifestFileName);
            FileInfo payload = CreateFile(
                sourceDirectory,
                $"{PayloadFileName}{DeploySuffix}");
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference(),
                        DeploySuffix)
                });
            ClickOnceStagingSession session = _layoutStager.Stage(layout);
            IDisposable update = session.BeginManifestFileInfoUpdate();

            session.Dispose();
            update.Dispose();
        }

        [Fact]
        public void EndManifestFileInfoUpdate_WhenMultipleRestoresFail_AttemptsAll()
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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        firstPayload,
                        "one.dll",
                        TestPublishLayoutEntryKind.Payload,
                        firstReference,
                        DeploySuffix),
                    CreateEntry(
                        secondPayload,
                        "two.dll",
                        TestPublishLayoutEntryKind.Payload,
                        secondReference,
                        DeploySuffix)
                });
            ClickOnceStagingSession session = _layoutStager.Stage(layout);
            IDisposable update = session.BeginManifestFileInfoUpdate();
            ClickOnceStagedFile[] mappedFiles = session.Payloads.Values
                .Where(file => file.FileInfoUpdateFile is not null)
                .ToArray();

            foreach (ClickOnceStagedFile mappedFile in mappedFiles)
            {
                File.WriteAllText(
                    mappedFile.Destination.FullName,
                    FileContents);
            }

            ClickOnceStagingException exception =
                Assert.Throws<ClickOnceStagingException>(
                    update.Dispose);
            AggregateException aggregate =
                Assert.IsType<AggregateException>(exception.InnerException);

            Assert.Equal(2, aggregate.InnerExceptions.Count);
            Assert.All(
                aggregate.InnerExceptions,
                inner => Assert.IsType<ClickOnceStagingException>(
                    inner));
            Assert.All(
                mappedFiles,
                mappedFile =>
                {
                    mappedFile.FileInfoUpdateFile!.Refresh();
                    Assert.True(mappedFile.FileInfoUpdateFile.Exists);
                    Assert.Equal(
                        mappedFile.FileInfoUpdateFile.FullName,
                        mappedFile.ManifestReference!.ResolvedPath);
                    File.Delete(mappedFile.Destination.FullName);
                });

            session.Dispose();
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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        reference,
                        DeploySuffix)
                });
            ClickOnceStagingSession session = _layoutStager.Stage(layout);
            DirectoryInfo stagingDirectory = session.Directory;
            session.BeginManifestFileInfoUpdate();
            string stablePath = Path.Combine(
                stagingDirectory.FullName,
                $"{PayloadFileName}{DeploySuffix}");
            File.WriteAllText(stablePath, FileContents);

            Assert.Throws<ClickOnceStagingException>(
                session.Dispose);

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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        payloadReference)
                });
            ClickOnceStagingSession session = _layoutStager.Stage(layout);
            DirectoryInfo stagingDirectory = session.Directory;

            Assert.NotEqual(
                payload.FullName,
                payloadReference.ResolvedPath);

            session.Dispose();
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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        missingPayload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        new FileReference())
                },
                new[] { diagnostic });

            ClickOnceStagingException exception =
                Assert.Throws<ClickOnceStagingException>(
                    () => _layoutStager.Stage(layout));

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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        firstReference),
                    CreateEntry(
                        payload,
                        PayloadFileName,
                        TestPublishLayoutEntryKind.Payload,
                        secondReference)
                });

            using ClickOnceStagingSession session = _layoutStager.Stage(layout);

            string stagedPath = Path.Combine(
                session.Directory.FullName,
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
            ResolvedClickOncePublishLayout layout = CreateApplicationLayout(
                applicationManifest,
                Substitute.For<IApplicationManifest>(),
                new[]
                {
                    CreateEntry(
                        payload,
                        $"sub/{PayloadFileName}",
                        TestPublishLayoutEntryKind.Payload,
                        firstReference),
                    CreateEntry(
                        payload,
                        $@"sub\{PayloadFileName}",
                        TestPublishLayoutEntryKind.Payload,
                        secondReference)
                });

            using ClickOnceStagingSession session = _layoutStager.Stage(layout);

            Assert.Equal(
                firstReference.ResolvedPath,
                secondReference.ResolvedPath);
            Assert.Equal(
                FileContents,
                File.ReadAllText(firstReference.ResolvedPath));
        }

        private static ResolvedClickOncePublishLayout
            CreateApplicationLayout(
            FileInfo applicationManifest,
            IApplicationManifest applicationModel,
            IEnumerable<TestPublishLayoutEntry>? payloads = null,
            IEnumerable<ClickOnceManifestDiagnostic>? diagnostics = null)
        {
            return new ResolvedClickOncePublishLayout(
                deployment: null,
                new ResolvedClickOnceApplication(
                    applicationManifest,
                    applicationModel,
                    (payloads ?? Array.Empty<TestPublishLayoutEntry>())
                        .Select(CreatePayload)),
                adjacentExecutables:
                    Array.Empty<ResolvedClickOnceAdjacentExecutable>(),
                diagnostics ?? Array.Empty<ClickOnceManifestDiagnostic>());
        }

        private static ResolvedClickOncePublishLayout CreateDeploymentLayout(
            FileInfo deploymentManifest,
            FileInfo applicationManifest,
            AssemblyReference applicationReference,
            IEnumerable<TestPublishLayoutEntry>? payloads = null,
            IEnumerable<TestPublishLayoutEntry>? adjacentExecutables = null)
        {
            IDeployManifest deployManifest = Substitute.For<IDeployManifest>();

            deployManifest.EntryPoint.Returns(applicationReference);

            return new ResolvedClickOncePublishLayout(
                new ResolvedClickOnceDeployment(
                    deploymentManifest,
                    deployManifest,
                    applicationReference),
                new ResolvedClickOnceApplication(
                    applicationManifest,
                    Substitute.For<IApplicationManifest>(),
                    (payloads ?? Array.Empty<TestPublishLayoutEntry>())
                        .Select(CreatePayload)),
                (adjacentExecutables ??
                    Array.Empty<TestPublishLayoutEntry>())
                    .Select(CreateAdjacentExecutable),
                diagnostics: Array.Empty<ClickOnceManifestDiagnostic>());
        }

        private static ResolvedClickOncePayload CreatePayload(
            TestPublishLayoutEntry entry)
        {
            BaseReference reference = entry.ManifestReference ??
                new FileReference();

            reference.ResolvedPath = entry.Source.FullName;
            reference.TargetPath = entry.TargetPath;
            ResolvedClickOncePayload payload = new(entry.Source, reference);

            Assert.Equal(
                entry.MappingAddedSuffix is not null,
                payload.IsFileExtensionMapped);

            return payload;
        }

        private static ResolvedClickOnceAdjacentExecutable
            CreateAdjacentExecutable(TestPublishLayoutEntry entry)
        {
            ClickOnceAdjacentExecutableKind kind = entry.Kind switch
            {
                TestPublishLayoutEntryKind.Setup =>
                    ClickOnceAdjacentExecutableKind.Setup,
                TestPublishLayoutEntryKind.Launcher =>
                    ClickOnceAdjacentExecutableKind.Launcher,
                _ => throw new ArgumentOutOfRangeException(nameof(entry))
            };

            return new ResolvedClickOnceAdjacentExecutable(
                entry.Source,
                kind);
        }

        private static TestPublishLayoutEntry CreateEntry(
            FileInfo source,
            string targetPath,
            TestPublishLayoutEntryKind kind,
            BaseReference? reference = null,
            string? mappingAddedSuffix = null)
        {
            return new TestPublishLayoutEntry(
                source,
                targetPath,
                kind,
                reference,
                mappingAddedSuffix);
        }

        private static bool PathsEqual(FileInfo left, FileInfo right)
        {
            return string.Equals(
                left.FullName,
                right.FullName,
                StringComparison.OrdinalIgnoreCase);
        }

        private enum TestPublishLayoutEntryKind
        {
            Payload,
            Setup,
            Launcher
        }

        private sealed class TestPublishLayoutEntry
        {
            internal TestPublishLayoutEntry(
                FileInfo source,
                string targetPath,
                TestPublishLayoutEntryKind kind,
                BaseReference? manifestReference,
                string? mappingAddedSuffix)
            {
                Source = source;
                TargetPath = targetPath;
                Kind = kind;
                ManifestReference = manifestReference;
                MappingAddedSuffix = mappingAddedSuffix;
            }

            internal FileInfo Source { get; }
            internal string TargetPath { get; }
            internal TestPublishLayoutEntryKind Kind { get; }
            internal BaseReference? ManifestReference { get; }
            internal string? MappingAddedSuffix { get; }
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
