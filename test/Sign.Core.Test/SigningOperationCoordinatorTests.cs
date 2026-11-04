// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class SigningOperationCoordinatorTests
    {
        [Fact]
        public async Task ExecuteAsync_OverlappingRequests_InvokesOneOwner()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                relativePath: "signed.bin");
            SigningSourceIdentity identity = CreateIdentity();
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int ownerCount = 0;

            async Task<FileInfo> OperateAsync()
            {
                Interlocked.Increment(ref ownerCount);
                await release.Task;

                return artifact;
            }

            Task<SigningOperationResult>[] tasks = Enumerable.Range(
                    start: 0,
                    count: 32)
                .Select(_ => coordinator.ExecuteAsync(identity, OperateAsync))
                .ToArray();
            release.SetResult();

            SigningOperationResult[] results = await Task.WhenAll(tasks);

            Assert.Equal(expected: 1, actual: ownerCount);
            Assert.All(results, result => Assert.Same(results[0], result));
        }

        [Fact]
        public async Task ExecuteAsync_CaseOnlyCanonicalIdentities_SharesOwner()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "SigningOperationCoordinator");
            SigningSourceIdentity first =
                SigningSourceIdentity.PhysicalFile(
                    Path.Combine(root, "directory", "..", "Source.bin"));
            SigningSourceIdentity second =
                SigningSourceIdentity.PhysicalFile(
                    Path.Combine(root, "source.BIN"));

            await AssertDistinctEqualIdentitiesShareOwnerAsync(first, second);
        }

        [Fact]
        public async Task ExecuteAsync_LexicallyDistinctCanonicalIdentities_SharesOwner()
        {
            SigningSourceIdentity first =
                SigningSourceIdentity.PhysicalFile(
                    Path.Combine(
                        Path.GetTempPath(),
                        "directory",
                        "..",
                        "source.bin"));
            SigningSourceIdentity second =
                SigningSourceIdentity.PhysicalFile(
                    Path.Combine(Path.GetTempPath(), "source.bin"));

            await AssertDistinctEqualIdentitiesShareOwnerAsync(first, second);
        }

        [Fact]
        public async Task ExecuteAsync_EquivalentRecursiveIdentities_SharesOwner()
        {
            SigningSourceIdentity first =
                SigningSourceIdentity.ContainerEntry(
                    SigningSourceIdentity.ContainerEntry(
                        parent: SigningSourceIdentity.PhysicalFile(
                            path: @"C:\bundle.appxbundle"),
                        entryPath: "application.appx"),
                    entryPath: "payload/file.dll");
            SigningSourceIdentity second =
                SigningSourceIdentity.ContainerEntry(
                    SigningSourceIdentity.ContainerEntry(
                        parent: SigningSourceIdentity.PhysicalFile(
                            path: @"c:\BUNDLE.appxbundle"),
                        entryPath: @".\application.appx"),
                    entryPath: @"payload\child\..\file.dll");

            await AssertDistinctEqualIdentitiesShareOwnerAsync(first, second);
        }

        [Fact]
        public async Task ExecuteAsync_CompletedEqualIdentity_ReusesResultAndSnapshot()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                relativePath: "signed.bin",
                contents: "signed content");
            SigningSourceIdentity firstIdentity =
                SigningSourceIdentity.PhysicalFile(
                    Path.Combine(
                        Path.GetTempPath(),
                        "directory",
                        "..",
                        "source.bin"));
            SigningSourceIdentity secondIdentity =
                SigningSourceIdentity.PhysicalFile(
                    Path.Combine(Path.GetTempPath(), "source.bin"));
            int ownerCount = 0;

            SigningOperationResult first = await coordinator.ExecuteAsync(
                firstIdentity,
                () =>
                {
                    ++ownerCount;
                    return Task.FromResult(artifact);
                });
            DirectoryInfo snapshotDirectory =
                Assert.Single(directoryService.Directories);

            Assert.Single(snapshotDirectory.EnumerateFiles());

            SigningOperationResult second = await coordinator.ExecuteAsync(
                secondIdentity,
                () => throw new InvalidOperationException());

            Assert.Same(first, second);
            Assert.Equal(expected: 1, actual: ownerCount);
            Assert.Single(snapshotDirectory.EnumerateFiles());
        }

        [Fact]
        public async Task ExecuteAsync_SimultaneousEntry_InvokesOneOwner()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            using Barrier barrier = new(participantCount: 9);
            FileInfo artifact = directory.CreateFile(
                relativePath: "signed.bin");
            SigningSourceIdentity identity = CreateIdentity();
            int ownerCount = 0;

            Task<SigningOperationResult>[] tasks = Enumerable.Range(
                    start: 0,
                    count: 8)
                .Select(
                    _ => Task.Factory.StartNew(
                        () =>
                        {
                            if (!barrier.SignalAndWait(
                                timeout: TimeSpan.FromSeconds(value: 5)))
                            {
                                throw new TimeoutException(
                                    message: "Simultaneous entry timed out.");
                            }

                            return coordinator.ExecuteAsync(
                                identity,
                                () =>
                                {
                                    Interlocked.Increment(ref ownerCount);

                                    return Task.FromResult(artifact);
                                });
                        },
                        cancellationToken: CancellationToken.None,
                        creationOptions: TaskCreationOptions.LongRunning,
                        scheduler: TaskScheduler.Default).Unwrap())
                .ToArray();

            Assert.True(
                condition: barrier.SignalAndWait(
                    timeout: TimeSpan.FromSeconds(value: 5)),
                userMessage: "Simultaneous entry timed out.");

            SigningOperationResult[] results = await Task.WhenAll(tasks)
                .WaitAsync(timeout: TimeSpan.FromSeconds(value: 10));

            Assert.Equal(expected: 1, actual: ownerCount);
            Assert.All(results, result => Assert.Same(results[0], result));
        }

        [Fact]
        public async Task ExecuteAsync_OwnerAndWaiters_ObserveSameException()
        {
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            SigningSourceIdentity identity = CreateIdentity();
            InvalidOperationException expected = new(message: "Failure.");
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<FileInfo> OperateAsync()
            {
                await release.Task;
                throw expected;
            }

            Task<SigningOperationResult>[] tasks = Enumerable.Range(
                    start: 0,
                    count: 8)
                .Select(_ => coordinator.ExecuteAsync(identity, OperateAsync))
                .ToArray();
            release.SetResult();

            foreach (Task<SigningOperationResult> task in tasks)
            {
                InvalidOperationException actual =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => task);

                Assert.Same(expected, actual);
            }
        }

        [Fact]
        public async Task ExecuteAsync_OperationThrowsSynchronously_PublishesException()
        {
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            InvalidOperationException expected = new(message: "Failure.");

            InvalidOperationException actual =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => coordinator.ExecuteAsync(
                            CreateIdentity(),
                            () => throw expected)
                        .WaitAsync(timeout: TimeSpan.FromSeconds(value: 5)));

            Assert.Same(expected, actual);
        }

        [Fact]
        public async Task ExecuteAsync_SharedArtifactIsUnavailable_SharesException()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            SigningSourceIdentity identity = CreateIdentity();
            FileInfo artifact = new(
                Path.Combine(directory.FullPath, "missing.bin"));
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<FileInfo> OperateAsync()
            {
                await release.Task;

                return artifact;
            }

            Task<SigningOperationResult>[] tasks = Enumerable.Range(
                    start: 0,
                    count: 8)
                .Select(_ => coordinator.ExecuteAsync(identity, OperateAsync))
                .ToArray();
            release.SetResult();
            InvalidOperationException? firstException = null;

            foreach (Task<SigningOperationResult> task in tasks)
            {
                InvalidOperationException exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => task);

                firstException ??= exception;
                Assert.Same(firstException, exception);
            }

            Assert.NotNull(firstException);
            Assert.Equal(
                "The signed artifact is unavailable.",
                firstException.Message);
        }

        [Fact]
        public async Task ExecuteAsync_OperationThrowsOperationCanceledException_SharesFault()
        {
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            SigningSourceIdentity identity = CreateIdentity();
            OperationCanceledException expected = new(message: "Canceled.");
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<FileInfo> OperateAsync()
            {
                await release.Task;
                throw expected;
            }

            Task<SigningOperationResult>[] tasks = Enumerable.Range(
                    start: 0,
                    count: 8)
                .Select(_ => coordinator.ExecuteAsync(identity, OperateAsync))
                .ToArray();
            release.SetResult();

            foreach (Task<SigningOperationResult> task in tasks)
            {
                OperationCanceledException actual =
                    await Assert.ThrowsAsync<OperationCanceledException>(
                        () => task);

                Assert.Same(expected, actual);
                Assert.True(task.IsFaulted);
                Assert.False(task.IsCanceled);
            }
        }

        [Fact]
        public async Task ExecuteAsync_IndependentCoordinators_RunIndependently()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator firstCoordinator =
                new(directoryService: directoryService);
            await using SigningOperationCoordinator secondCoordinator =
                new(directoryService: directoryService);
            FileInfo firstArtifact = directory.CreateFile(
                relativePath: "first.bin");
            FileInfo secondArtifact = directory.CreateFile(
                relativePath: "second.bin");
            SigningSourceIdentity identity = CreateIdentity();
            int ownerCount = 0;

            Task<SigningOperationResult> first =
                firstCoordinator.ExecuteAsync(
                    identity,
                    () =>
                    {
                        Interlocked.Increment(ref ownerCount);
                        return Task.FromResult(firstArtifact);
                    });
            Task<SigningOperationResult> second =
                secondCoordinator.ExecuteAsync(
                    identity,
                    () =>
                    {
                        Interlocked.Increment(ref ownerCount);
                        return Task.FromResult(secondArtifact);
                    });

            Assert.NotSame(await first, await second);
            Assert.Equal(expected: 2, actual: ownerCount);
        }

        [Fact]
        public async Task ExecuteAsync_IndependentCoordinatorFails_IsolatesFailure()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator firstCoordinator =
                new(directoryService: directoryService);
            await using SigningOperationCoordinator secondCoordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                "second.bin",
                "signed content");
            SigningSourceIdentity identity = CreateIdentity();
            InvalidOperationException expected = new(message: "Failure.");

            Task<SigningOperationResult> first =
                firstCoordinator.ExecuteAsync(
                    identity,
                    () => Task.FromException<FileInfo>(expected));
            Task<SigningOperationResult> second =
                secondCoordinator.ExecuteAsync(
                    identity,
                    () => Task.FromResult(artifact));

            InvalidOperationException actual =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => first);
            SigningOperationResult result = await second;

            Assert.Same(expected, actual);
            Assert.NotNull(result);
        }

        [Fact]
        public async Task ExecuteAsync_ArtifactIsNotYetAvailable_DoesNotPublishResult()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = new(
                Path.Combine(directory.FullPath, "signed.bin"));
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<FileInfo> OperateAsync()
            {
                await release.Task;
                File.WriteAllText(
                    path: artifact.FullName,
                    contents: "signed");

                return artifact;
            }

            Task<SigningOperationResult> owner =
                coordinator.ExecuteAsync(CreateIdentity(), OperateAsync);

            Assert.False(owner.IsCompleted);

            release.SetResult();
            SigningOperationResult result = await owner;
            FileInfo materialized = result.Materialize(
                new FileInfo(
                    Path.Combine(
                        directory.FullPath,
                        "output",
                        "signed.bin")));

            Assert.Equal("signed", File.ReadAllText(materialized.FullName));
        }

        [Fact]
        public async Task ExecuteAsync_UnavailableArtifact_Throws()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = new(
                Path.Combine(directory.FullPath, "missing.bin"));

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => coordinator.ExecuteAsync(
                        CreateIdentity(),
                        () => Task.FromResult(artifact)));

            Assert.Equal(
                "The signed artifact is unavailable.",
                exception.Message);
        }

        [Fact]
        public async Task ExecuteAsync_SnapshotDirectoryIsUnavailable_ThrowsCopyException()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                relativePath: "signed.bin");
            DirectoryInfo snapshotDirectory =
                Assert.Single(directoryService.Directories);
            snapshotDirectory.Delete();

            await Assert.ThrowsAsync<DirectoryNotFoundException>(
                () => coordinator.ExecuteAsync(
                    CreateIdentity(),
                    () => Task.FromResult(artifact)));
        }

        [Fact]
        public async Task ExecuteAsync_MissingArtifact_Throws()
        {
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => coordinator.ExecuteAsync(
                        CreateIdentity(),
                        () => Task.FromResult<FileInfo>(null!)));

            Assert.Equal(
                "The signing operation returned no artifact.",
                exception.Message);
        }

        [Fact]
        public async Task ExecuteAsync_NullTask_Throws()
        {
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => coordinator.ExecuteAsync(
                        CreateIdentity(),
                        () => null!));

            Assert.Equal(
                "The signing operation returned no task.",
                exception.Message);
        }

        [Fact]
        public async Task ExecuteAsync_ResultUsedByMultipleLayouts_MaterializesSnapshot()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                Path.Combine("owner", "signed.bin"),
                "signed content");
            SigningSourceIdentity identity = CreateIdentity();

            Task<SigningOperationResult> first =
                coordinator.ExecuteAsync(
                    identity,
                    () => Task.FromResult(artifact));
            Task<SigningOperationResult> second =
                coordinator.ExecuteAsync(
                    identity,
                    () => throw new InvalidOperationException());
            SigningOperationResult[] results =
                await Task.WhenAll(first, second);
            FileInfo firstMaterialization =
                results[0].Materialize(
                    new FileInfo(
                        Path.Combine(
                            directory.FullPath,
                            "layout1",
                            "signed.bin")));
            FileInfo secondMaterialization =
                results[1].Materialize(
                    new FileInfo(
                        Path.Combine(
                            directory.FullPath,
                            "layout2",
                            "signed.bin")));
            FileInfo[] materialized =
            [
                firstMaterialization,
                secondMaterialization
            ];

            Assert.Same(results[0], results[1]);
            Assert.All(
                materialized,
                file => Assert.Equal(
                    "signed content",
                    File.ReadAllText(file.FullName)));
        }

        [Fact]
        public async Task ExecuteArtifactAsync_LayoutUsedByMultipleOutputs_MaterializesAllFiles()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService);
            FileInfo primary = directory.CreateFile(
                Path.Combine("owner", "signed.application"),
                "signed manifest");
            FileInfo dependency = directory.CreateFile(
                Path.Combine(
                    "owner",
                    "Application Files",
                    "payload.dll.deploy"),
                "signed payload");

            SigningOperationResult result =
                await coordinator.ExecuteArtifactAsync(
                    CreateIdentity(),
                    () => Task.FromResult(
                        SigningOperationArtifact.Layout(
                            primary,
                            primary.Directory!)));
            FileInfo first = result.Materialize(
                new FileInfo(
                    Path.Combine(
                        directory.FullPath,
                        "first",
                        "app.application")));
            FileInfo second = result.Materialize(
                new FileInfo(
                    Path.Combine(
                        directory.FullPath,
                        "second",
                        "renamed.application")));

            Assert.Equal(
                "signed manifest",
                File.ReadAllText(first.FullName));
            Assert.Equal(
                "signed manifest",
                File.ReadAllText(second.FullName));
            Assert.Equal(
                "signed payload",
                File.ReadAllText(
                    Path.Combine(
                        first.DirectoryName!,
                        "Application Files",
                        dependency.Name)));
            Assert.Equal(
                "signed payload",
                File.ReadAllText(
                    Path.Combine(
                        second.DirectoryName!,
                        "Application Files",
                        dependency.Name)));
        }

        [Fact]
        public async Task ExecuteAsync_OwnerArtifactDeleted_ResultStillMaterializes()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                "signed.bin",
                "signed content");
            SigningOperationResult result = await coordinator.ExecuteAsync(
                CreateIdentity(),
                () => Task.FromResult(artifact));

            artifact.Delete();
            FileInfo materialized = result.Materialize(
                new FileInfo(
                    Path.Combine(
                        directory.FullPath,
                        "output",
                        "signed.bin")));

            Assert.Equal(
                "signed content",
                File.ReadAllText(materialized.FullName));
        }

        [Fact]
        public async Task ExecuteAsync_OwnerArtifactReplaced_ResultMaterializesSnapshot()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                "signed.bin",
                "signed content");
            SigningOperationResult result = await coordinator.ExecuteAsync(
                CreateIdentity(),
                () => Task.FromResult(artifact));

            File.WriteAllText(
                path: artifact.FullName,
                contents: "replacement");
            FileInfo materialized = result.Materialize(
                new FileInfo(
                    Path.Combine(
                        directory.FullPath,
                        "output",
                        "signed.bin")));

            Assert.Equal(
                "signed content",
                File.ReadAllText(materialized.FullName));
        }

        [Fact]
        public async Task Materialize_PreexistingDestination_IsAtomicallyReplaced()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                "signed.bin",
                "complete signed content");
            FileInfo destination = directory.CreateFile(
                Path.Combine("output", "signed.bin"),
                "old");
            SigningOperationResult result = await coordinator.ExecuteAsync(
                CreateIdentity(),
                () => Task.FromResult(artifact));

            FileInfo materialized = result.Materialize(destination);

            Assert.Equal(
                "complete signed content",
                File.ReadAllText(materialized.FullName));
            Assert.Empty(
                destination.Directory!.EnumerateFiles(".sign-*.tmp"));
        }

        [Fact]
        public async Task DisposeAsync_ExistingSnapshots_DeletesSnapshotsAndPreventsMaterialization()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                "signed.bin",
                "signed content");
            SigningOperationResult result = await coordinator.ExecuteAsync(
                CreateIdentity(),
                () => Task.FromResult(artifact));
            DirectoryInfo snapshotDirectory =
                Assert.Single(directoryService.Directories);

            Assert.True(snapshotDirectory.Exists);

            await coordinator.DisposeAsync();
            snapshotDirectory.Refresh();

            Assert.False(snapshotDirectory.Exists);
            Assert.Throws<ObjectDisposedException>(
                () => result.Materialize(
                    new FileInfo(
                        Path.Combine(
                            directory.FullPath,
                            "output",
                            "signed.bin"))));
        }

        [Fact]
        public async Task DisposeAsync_ActiveOwner_WaitsForOwnerThenDeletesSnapshots()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                "signed.bin",
                "signed content");
            TaskCompletionSource entered = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<FileInfo> OperateAsync()
            {
                entered.SetResult();
                await release.Task;

                return artifact;
            }

            Task<SigningOperationResult> operation =
                coordinator.ExecuteAsync(
                    CreateIdentity(),
                    OperateAsync);
            await entered.Task.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
            DirectoryInfo snapshotDirectory =
                Assert.Single(directoryService.Directories);

            Task disposeTask = coordinator.DisposeAsync().AsTask();

            Assert.False(disposeTask.IsCompleted);

            release.SetResult();
            SigningOperationResult result = await operation.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
            await disposeTask.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
            snapshotDirectory.Refresh();

            Assert.NotNull(result);
            Assert.False(snapshotDirectory.Exists);
        }

        [Fact]
        public async Task DisposeAsync_OwnerStartsNestedOperationBeforeDispose_WaitsForNestedOperation()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo ownerArtifact = directory.CreateFile(
                "owner.bin",
                "owner content");
            FileInfo nestedArtifact = directory.CreateFile(
                "nested.bin",
                "nested content");
            SigningSourceIdentity ownerIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\owner.bin");
            SigningSourceIdentity nestedIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\nested.bin");
            TaskCompletionSource nestedEntered = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releaseNested = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource nestedCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releaseOwner = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<FileInfo> RunOwnerAsync()
            {
                await coordinator.WaitAsync(
                    coordinator.ExecuteAsync(
                        nestedIdentity,
                        async () =>
                        {
                            nestedEntered.SetResult();
                            await releaseNested.Task;

                            return nestedArtifact;
                        }));
                nestedCompleted.SetResult();
                await releaseOwner.Task;

                return ownerArtifact;
            }

            Task<SigningOperationResult> owner =
                coordinator.ExecuteAsync(
                    ownerIdentity,
                    RunOwnerAsync);
            await nestedEntered.Task.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
            DirectoryInfo snapshotDirectory =
                Assert.Single(directoryService.Directories);

            Task disposeTask = coordinator.DisposeAsync().AsTask();
            snapshotDirectory.Refresh();

            Assert.False(disposeTask.IsCompleted);
            Assert.True(snapshotDirectory.Exists);

            releaseNested.SetResult();
            await nestedCompleted.Task.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));

            Assert.False(disposeTask.IsCompleted);

            releaseOwner.SetResult();
            _ = await owner.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
            await disposeTask.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
            snapshotDirectory.Refresh();

            Assert.False(snapshotDirectory.Exists);
        }

        [Fact]
        public async Task DisposeAsync_OwnerStartsNestedOperationAfterDispose_NestedCallThrowsObjectDisposed()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                "owner.bin",
                "owner content");
            SigningSourceIdentity ownerIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\owner.bin");
            SigningSourceIdentity nestedIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\nested.bin");
            TaskCompletionSource ownerEntered = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource attemptNested = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<Exception?> nestedException = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<FileInfo> RunOwnerAsync()
            {
                ownerEntered.SetResult();
                await attemptNested.Task;
                Exception? exception = null;

                try
                {
                    await coordinator.ExecuteAsync(
                            nestedIdentity,
                            () => Task.FromResult(artifact))
                        .ConfigureAwait(continueOnCapturedContext: false);
                }
                catch (Exception caughtException)
                {
                    exception = caughtException;
                }

                nestedException.SetResult(exception);

                return artifact;
            }

            Task<SigningOperationResult> owner =
                coordinator.ExecuteAsync(
                    ownerIdentity,
                    RunOwnerAsync);
            await ownerEntered.Task.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
            DirectoryInfo snapshotDirectory =
                Assert.Single(directoryService.Directories);

            Task disposeTask = coordinator.DisposeAsync().AsTask();

            Assert.False(disposeTask.IsCompleted);

            attemptNested.SetResult();
            Exception? exception = await nestedException.Task.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
            _ = await owner.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
            await disposeTask.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
            snapshotDirectory.Refresh();

            Assert.IsType<ObjectDisposedException>(exception);
            Assert.False(snapshotDirectory.Exists);
        }

        [Fact]
        public async Task DisposeAsync_ConcurrentCalls_DeleteSnapshotsOnce()
        {
            using TestDirectory directory = new();
            using DirectoryServiceSpy directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                "signed.bin",
                "signed content");
            TaskCompletionSource entered = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<FileInfo> OperateAsync()
            {
                entered.SetResult();
                await release.Task;

                return artifact;
            }

            Task<SigningOperationResult> operation =
                coordinator.ExecuteAsync(
                    CreateIdentity(),
                    OperateAsync);
            await entered.Task.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
            DirectoryInfo snapshotDirectory =
                Assert.Single(directoryService.Directories);

            Task firstDispose = coordinator.DisposeAsync().AsTask();
            Task secondDispose = coordinator.DisposeAsync().AsTask();

            Assert.False(firstDispose.IsCompleted);
            Assert.False(secondDispose.IsCompleted);

            release.SetResult();
            _ = await operation.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
            await Task.WhenAll(firstDispose, secondDispose).WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
            snapshotDirectory.Refresh();

            Assert.False(snapshotDirectory.Exists);
            Assert.Equal(expected: 1, actual: directoryService.DeleteCount);
        }

        [Fact]
        public async Task DisposeAsync_DeleteFails_AllCallersObserveFailure()
        {
            using TestDirectory directory = new();
            IOException deleteException = new("Delete failed.");
            using DirectoryServiceSpy directoryService = new()
            {
                DeleteException = deleteException
            };
            SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                "signed.bin",
                "signed content");
            TaskCompletionSource entered = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<FileInfo> OperateAsync()
            {
                entered.SetResult();
                await release.Task;

                return artifact;
            }

            Task<SigningOperationResult> operation =
                coordinator.ExecuteAsync(
                    CreateIdentity(),
                    OperateAsync);
            await entered.Task.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));

            Task firstDispose = coordinator.DisposeAsync().AsTask();
            Task secondDispose = coordinator.DisposeAsync().AsTask();

            Assert.False(firstDispose.IsCompleted);
            Assert.False(secondDispose.IsCompleted);

            release.SetResult();
            _ = await operation.WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
            IOException first = await Assert.ThrowsAsync<IOException>(
                () => firstDispose.WaitAsync(
                    timeout: TimeSpan.FromSeconds(value: 5)));
            IOException second = await Assert.ThrowsAsync<IOException>(
                () => secondDispose.WaitAsync(
                    timeout: TimeSpan.FromSeconds(value: 5)));
            IOException later = await Assert.ThrowsAsync<IOException>(
                () => coordinator.DisposeAsync().AsTask());

            Assert.Same(deleteException, first);
            Assert.Same(deleteException, second);
            Assert.Same(deleteException, later);
            Assert.Equal(expected: 1, actual: directoryService.DeleteCount);
            Assert.Throws<ObjectDisposedException>(
                () =>
                {
                    _ = coordinator.ExecuteAsync(
                        CreateIdentity(),
                        () => throw new InvalidOperationException());
                });
        }

        [Fact]
        public async Task ExecuteAsync_AfterDisposeAsync_Throws()
        {
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            await coordinator.DisposeAsync();

            Assert.Throws<ObjectDisposedException>(
                () =>
                {
                    _ = coordinator.ExecuteAsync(
                        CreateIdentity(),
                        () => throw new InvalidOperationException());
                });
        }

        [Fact]
        public async Task DisposeAsync_CalledTwice_IsIdempotent()
        {
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);

            await coordinator.DisposeAsync();
            await coordinator.DisposeAsync();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TryDelete_PathDoesNotExist_ReturnsNull(
            bool parentDirectoryExists)
        {
            using TestDirectory directory = new();
            string parentPath = Path.Combine(
                directory.FullPath,
                "parent");

            if (parentDirectoryExists)
            {
                Directory.CreateDirectory(parentPath);
            }

            FileInfo file = new(Path.Combine(parentPath, "missing.bin"));

            Exception? exception = SigningOperationFile.TryDelete(file);

            Assert.Null(exception);
        }

        [Fact]
        public async Task WaitAsync_DirectSelfAwaitAfterAwait_Throws()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                relativePath: "signed.bin");
            SigningSourceIdentity identity = CreateIdentity();
            bool nestedOperationRan = false;

            async Task<FileInfo> OperateAsync()
            {
                await Task.Yield();
                await coordinator.WaitAsync(
                    coordinator.ExecuteAsync(
                        identity,
                        () =>
                        {
                            nestedOperationRan = true;
                            return Task.FromResult(artifact);
                        }));

                return artifact;
            }

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => coordinator.ExecuteAsync(identity, OperateAsync)
                        .WaitAsync(
                            timeout: TimeSpan.FromSeconds(value: 5)));

            Assert.Equal(
                "A signing operation cannot await itself.",
                exception.Message);
            Assert.False(nestedOperationRan);
        }

        [Fact]
        public async Task WaitAsync_NestedDependencyCycle_FaultsAllOperations()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo firstArtifact = directory.CreateFile(
                relativePath: "first.bin");
            FileInfo secondArtifact = directory.CreateFile(
                relativePath: "second.bin");
            SigningSourceIdentity firstIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\first.bin");
            SigningSourceIdentity secondIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\second.bin");
            Task<SigningOperationResult>? secondTask = null;
            bool duplicateFirstOperationRan = false;

            async Task<FileInfo> RunFirstAsync()
            {
                secondTask = coordinator.ExecuteAsync(
                    secondIdentity,
                    async () =>
                    {
                        Task<SigningOperationResult> duplicateFirst =
                            coordinator.ExecuteAsync(
                                firstIdentity,
                                () =>
                                {
                                    duplicateFirstOperationRan = true;

                                    return Task.FromResult(firstArtifact);
                                });
                        _ = await coordinator.WaitAsync(duplicateFirst);

                        return secondArtifact;
                    });
                _ = await coordinator.WaitAsync(secondTask);

                return firstArtifact;
            }

            Task<SigningOperationResult> firstTask =
                coordinator.ExecuteAsync(
                    firstIdentity,
                    RunFirstAsync);
            InvalidOperationException firstException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => firstTask.WaitAsync(
                        timeout: TimeSpan.FromSeconds(value: 5)));
            Task<SigningOperationResult> actualSecondTask =
                Assert.IsType<Task<SigningOperationResult>>(secondTask);

            InvalidOperationException secondException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => actualSecondTask.WaitAsync(
                        timeout: TimeSpan.FromSeconds(value: 5)));

            Assert.Equal(
                "A signing operation dependency cycle was detected.",
                firstException.Message);
            Assert.Same(firstException, secondException);
            Assert.False(duplicateFirstOperationRan);

            InvalidOperationException waiterException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => coordinator.ExecuteAsync(
                        firstIdentity,
                        () => throw new InvalidOperationException()));

            Assert.Same(firstException, waiterException);
            await coordinator.DisposeAsync().AsTask().WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
        }

        [Fact]
        public async Task WaitAsync_IndependentDependencyCycle_FaultsAllOperations()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo firstArtifact = directory.CreateFile(
                relativePath: "first.bin");
            FileInfo secondArtifact = directory.CreateFile(
                relativePath: "second.bin");
            SigningSourceIdentity firstIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\first.bin");
            SigningSourceIdentity secondIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\second.bin");
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<SigningOperationResult>? firstTask = null;
            Task<SigningOperationResult>? secondTask = null;

            async Task<FileInfo> RunFirstAsync()
            {
                await release.Task;
                _ = await coordinator.WaitAsync(secondTask!);

                return firstArtifact;
            }

            async Task<FileInfo> RunSecondAsync()
            {
                await release.Task;
                _ = await coordinator.WaitAsync(firstTask!);

                return secondArtifact;
            }

            firstTask = coordinator.ExecuteAsync(
                firstIdentity,
                RunFirstAsync);
            secondTask = coordinator.ExecuteAsync(
                secondIdentity,
                RunSecondAsync);
            release.SetResult();

            InvalidOperationException firstException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => firstTask!.WaitAsync(
                        timeout: TimeSpan.FromSeconds(value: 5)));
            InvalidOperationException secondException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => secondTask!.WaitAsync(
                        timeout: TimeSpan.FromSeconds(value: 5)));

            Assert.Equal(
                "A signing operation dependency cycle was detected.",
                firstException.Message);
            Assert.Same(firstException, secondException);

            InvalidOperationException waiterException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => coordinator.ExecuteAsync(
                        secondIdentity,
                        () => throw new InvalidOperationException()));

            Assert.Same(firstException, waiterException);
            await coordinator.DisposeAsync().AsTask().WaitAsync(
                timeout: TimeSpan.FromSeconds(value: 5));
        }

        [Fact]
        public async Task WaitAsync_MilestoneDependencyCycle_FaultsAllOperations()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo firstArtifact = directory.CreateFile(
                relativePath: "first.bin");
            FileInfo secondArtifact = directory.CreateFile(
                relativePath: "second.bin");
            SigningSourceIdentity firstIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\first.bin");
            SigningSourceIdentity secondIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\second.bin");
            TaskCompletionSource milestone = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<SigningOperationResult>? secondTask = null;

            async Task<FileInfo> RunFirstAsync()
            {
                secondTask = coordinator.ExecuteAsync(
                    secondIdentity,
                    async () =>
                    {
                        Task<SigningOperationResult> duplicateFirst =
                            coordinator.ExecuteAsync(
                                firstIdentity,
                                () => Task.FromResult(firstArtifact));
                        _ = await coordinator.WaitAsync(duplicateFirst);

                        return secondArtifact;
                    });
                _ = await coordinator.WaitAsync(
                    secondTask,
                    milestone.Task);

                return firstArtifact;
            }

            Task<SigningOperationResult> firstTask =
                coordinator.ExecuteAsync(
                    firstIdentity,
                    RunFirstAsync);
            InvalidOperationException firstException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => firstTask.WaitAsync(
                        timeout: TimeSpan.FromSeconds(value: 5)));
            Task<SigningOperationResult> actualSecondTask =
                Assert.IsType<Task<SigningOperationResult>>(secondTask);
            InvalidOperationException secondException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => actualSecondTask.WaitAsync(
                        timeout: TimeSpan.FromSeconds(value: 5)));

            Assert.Equal(
                "A signing operation dependency cycle was detected.",
                firstException.Message);
            Assert.Same(firstException, secondException);
        }

        [Fact]
        public async Task WaitAsync_MilestoneCompletesBeforeReverseWait_Completes()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo ownerArtifact = directory.CreateFile(
                relativePath: "owner.bin");
            FileInfo childArtifact = directory.CreateFile(
                relativePath: "child.bin");
            SigningSourceIdentity ownerIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\owner.bin");
            SigningSourceIdentity childIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\child.bin");
            TaskCompletionSource milestone = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releaseChild = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<SigningOperationResult>? ownerTask = null;
            Task<SigningOperationResult>? childTask = null;
            System.Reflection.FieldInfo gateField =
                typeof(SigningOperationCoordinator).GetField(
                    "_gate",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)!;
            object gate = gateField.GetValue(coordinator)!;

            async Task<FileInfo> RunOwnerAsync()
            {
                childTask = coordinator.ExecuteAsync(
                    childIdentity,
                    async () =>
                    {
                        await releaseChild.Task;
                        Task<SigningOperationResult> ownerWait;

                        lock (gate)
                        {
                            milestone.SetResult();
                            ownerWait = coordinator.WaitAsync(ownerTask!);
                        }

                        _ = await ownerWait;
                        return childArtifact;
                    });
                Task completed = await coordinator.WaitAsync(
                    childTask,
                    milestone.Task);

                Assert.Same(milestone.Task, completed);

                return ownerArtifact;
            }

            ownerTask = coordinator.ExecuteAsync(
                ownerIdentity,
                RunOwnerAsync);
            Task<SigningOperationResult> actualChildTask =
                Assert.IsType<Task<SigningOperationResult>>(childTask);

            releaseChild.SetResult();

            await Task.WhenAll(
                    ownerTask,
                    actualChildTask)
                .WaitAsync(timeout: TimeSpan.FromSeconds(value: 5));
        }

        [Fact]
        public async Task WaitAsync_CrossCoordinatorWaits_FaultBothOperations()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub firstDirectoryService = new();
            using DirectoryServiceStub secondDirectoryService = new();
            await using SigningOperationCoordinator firstCoordinator =
                new(directoryService: firstDirectoryService);
            await using SigningOperationCoordinator secondCoordinator =
                new(directoryService: secondDirectoryService);
            FileInfo firstArtifact = directory.CreateFile(
                relativePath: "first.bin");
            FileInfo secondArtifact = directory.CreateFile(
                relativePath: "second.bin");
            SigningSourceIdentity firstIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\first.bin");
            SigningSourceIdentity secondIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\second.bin");
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<SigningOperationResult>? firstTask = null;
            Task<SigningOperationResult>? secondTask = null;

            firstTask = firstCoordinator.ExecuteAsync(
                firstIdentity,
                async () =>
                {
                    await release.Task;
                    _ = await secondCoordinator.WaitAsync(secondTask!);

                    return firstArtifact;
                });
            secondTask = secondCoordinator.ExecuteAsync(
                secondIdentity,
                async () =>
                {
                    await release.Task;
                    _ = await firstCoordinator.WaitAsync(firstTask);

                    return secondArtifact;
                });

            release.SetResult();

            InvalidOperationException firstException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => firstTask.WaitAsync(
                        timeout: TimeSpan.FromSeconds(value: 5)));
            InvalidOperationException secondException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => secondTask.WaitAsync(
                        timeout: TimeSpan.FromSeconds(value: 5)));

            Assert.Equal(
                "A signing operation cannot await work from another " +
                "coordinator.",
                firstException.Message);
            Assert.Equal(firstException.Message, secondException.Message);
        }

        [Fact]
        public async Task WaitAsync_CompletedCrossCoordinatorResult_Throws()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub firstDirectoryService = new();
            using DirectoryServiceStub secondDirectoryService = new();
            await using SigningOperationCoordinator firstCoordinator =
                new(directoryService: firstDirectoryService);
            await using SigningOperationCoordinator secondCoordinator =
                new(directoryService: secondDirectoryService);
            FileInfo firstArtifact = directory.CreateFile(
                relativePath: "first.bin");
            FileInfo secondArtifact = directory.CreateFile(
                relativePath: "second.bin");
            Task<SigningOperationResult> firstTask =
                firstCoordinator.ExecuteAsync(
                    SigningSourceIdentity.PhysicalFile(
                        path: @"C:\first.bin"),
                    () => Task.FromResult(firstArtifact));
            _ = await firstTask;

            Task<SigningOperationResult> secondTask =
                secondCoordinator.ExecuteAsync(
                    SigningSourceIdentity.PhysicalFile(
                        path: @"C:\second.bin"),
                    async () =>
                    {
                        _ = await firstCoordinator.WaitAsync(firstTask);

                        return secondArtifact;
                    });
            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => secondTask.WaitAsync(
                        timeout: TimeSpan.FromSeconds(value: 5)));

            Assert.Equal(
                "A signing operation cannot await work from another " +
                "coordinator.",
                exception.Message);
        }

        [Fact]
        public async Task WaitAsync_CompletedForeignResultTask_Throws()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub firstDirectoryService = new();
            using DirectoryServiceStub secondDirectoryService = new();
            await using SigningOperationCoordinator firstCoordinator =
                new(directoryService: firstDirectoryService);
            await using SigningOperationCoordinator secondCoordinator =
                new(directoryService: secondDirectoryService);
            FileInfo firstArtifact = directory.CreateFile(
                relativePath: "first.bin");
            FileInfo secondArtifact = directory.CreateFile(
                relativePath: "second.bin");
            Task<SigningOperationResult> firstTask =
                firstCoordinator.ExecuteAsync(
                    SigningSourceIdentity.PhysicalFile(
                        path: @"C:\first.bin"),
                    () => Task.FromResult(firstArtifact));
            _ = await firstTask;

            Task<SigningOperationResult> secondTask =
                secondCoordinator.ExecuteAsync(
                    SigningSourceIdentity.PhysicalFile(
                        path: @"C:\second.bin"),
                    async () =>
                    {
                        _ = await secondCoordinator.WaitAsync(firstTask);

                        return secondArtifact;
                    });
            ArgumentException exception =
                await Assert.ThrowsAsync<ArgumentException>(
                    () => secondTask.WaitAsync(
                        timeout: TimeSpan.FromSeconds(value: 5)));

            Assert.Equal("resultTask", exception.ParamName);
            Assert.Equal(
                "The result task does not belong to this coordinator. " +
                "(Parameter 'resultTask')",
                exception.Message);
        }

        [Fact]
        public async Task WaitAsync_ChildWaitsForNonWaitingOwner_Completes()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo ownerArtifact = directory.CreateFile(
                relativePath: "owner.bin");
            FileInfo childArtifact = directory.CreateFile(
                relativePath: "child.bin");
            SigningSourceIdentity ownerIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\owner.bin");
            SigningSourceIdentity childIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\child.bin");
            TaskCompletionSource allowChildWait = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releaseOwner = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<SigningOperationResult>? ownerTask = null;
            Task<SigningOperationResult>? childTask = null;

            async Task<FileInfo> RunOwnerAsync()
            {
                childTask = coordinator.ExecuteAsync(
                    childIdentity,
                    async () =>
                    {
                        await allowChildWait.Task;
                        _ = await coordinator.WaitAsync(ownerTask!);

                        return childArtifact;
                    });
                await releaseOwner.Task;

                return ownerArtifact;
            }

            ownerTask = coordinator.ExecuteAsync(
                ownerIdentity,
                RunOwnerAsync);
            Task<SigningOperationResult> actualChildTask =
                Assert.IsType<Task<SigningOperationResult>>(childTask);

            allowChildWait.SetResult();
            releaseOwner.SetResult();

            SigningOperationResult owner =
                await ownerTask.WaitAsync(
                    timeout: TimeSpan.FromSeconds(value: 5));
            SigningOperationResult child =
                await actualChildTask.WaitAsync(
                    timeout: TimeSpan.FromSeconds(value: 5));

            Assert.NotNull(owner);
            Assert.NotNull(child);
        }

        [Fact]
        public async Task WaitAsync_SharedPrerequisite_CompletesOnce()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo prerequisiteArtifact = directory.CreateFile(
                relativePath: "prerequisite.bin");
            FileInfo firstArtifact = directory.CreateFile(
                relativePath: "first.bin");
            FileInfo secondArtifact = directory.CreateFile(
                relativePath: "second.bin");
            SigningSourceIdentity prerequisiteIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\prerequisite.bin");
            SigningSourceIdentity firstIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\first.bin");
            SigningSourceIdentity secondIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\second.bin");
            TaskCompletionSource releasePrerequisite = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int prerequisiteOperationCount = 0;

            async Task<FileInfo> RunPrerequisiteAsync()
            {
                ++prerequisiteOperationCount;
                await releasePrerequisite.Task;

                return prerequisiteArtifact;
            }

            Task<SigningOperationResult> prerequisiteTask =
                coordinator.ExecuteAsync(
                    prerequisiteIdentity,
                    RunPrerequisiteAsync);
            Task<SigningOperationResult> firstTask =
                coordinator.ExecuteAsync(
                    firstIdentity,
                    async () =>
                    {
                        _ = await coordinator.WaitAsync(
                            coordinator.ExecuteAsync(
                                prerequisiteIdentity,
                                RunPrerequisiteAsync));

                        return firstArtifact;
                    });
            Task<SigningOperationResult> secondTask =
                coordinator.ExecuteAsync(
                    secondIdentity,
                    async () =>
                    {
                        _ = await coordinator.WaitAsync(
                            coordinator.ExecuteAsync(
                                prerequisiteIdentity,
                                RunPrerequisiteAsync));

                        return secondArtifact;
                    });

            releasePrerequisite.SetResult();
            await Task.WhenAll(
                    prerequisiteTask,
                    firstTask,
                    secondTask)
                .WaitAsync(timeout: TimeSpan.FromSeconds(value: 5));

            Assert.Equal(
                expected: 1,
                actual: prerequisiteOperationCount);
        }

        [Fact]
        public async Task ExecuteAsync_CompletedOperationFromCapturedContext_ReusesResult()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                relativePath: "signed.bin");
            SigningSourceIdentity identity = CreateIdentity();
            TaskCompletionSource releaseDuplicate = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<SigningOperationResult>? duplicateTask = null;
            bool duplicateOperationRan = false;

            Task<FileInfo> OperateAsync()
            {
                duplicateTask = Task.Run(
                    async () =>
                    {
                        await releaseDuplicate.Task;

                        return await coordinator.ExecuteAsync(
                            identity,
                            () =>
                            {
                                duplicateOperationRan = true;
                                return Task.FromResult(artifact);
                            });
                    });

                return Task.FromResult(artifact);
            }

            SigningOperationResult owner =
                await coordinator.ExecuteAsync(identity, OperateAsync);
            Assert.NotNull(duplicateTask);

            // Releasing after owner completion exercises only the completed
            // result fast path. In-flight claims from the owner's captured
            // context are rejected as self-await.
            releaseDuplicate.SetResult();
            SigningOperationResult duplicate =
                await duplicateTask.WaitAsync(
                    timeout: TimeSpan.FromSeconds(value: 5));

            Assert.Same(owner, duplicate);
            Assert.False(duplicateOperationRan);
        }

        [Fact]
        public async Task ExecuteAsync_ExternalWaiter_ReusesOwner()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                relativePath: "signed.bin");
            SigningSourceIdentity identity = CreateIdentity();
            TaskCompletionSource entered = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int ownerCount = 0;

            async Task<FileInfo> OperateAsync()
            {
                Interlocked.Increment(ref ownerCount);
                entered.SetResult();
                await release.Task;

                return artifact;
            }

            Task<SigningOperationResult> owner =
                coordinator.ExecuteAsync(identity, OperateAsync);
            await entered.Task;
            Task<SigningOperationResult> waiter =
                coordinator.ExecuteAsync(
                    identity,
                    () => throw new InvalidOperationException());
            release.SetResult();

            SigningOperationResult[] results =
                await Task.WhenAll(owner, waiter);

            Assert.Equal(expected: 1, actual: ownerCount);
            Assert.Same(results[0], results[1]);
        }

        [Fact]
        public async Task ExecuteArtifactAsync_ReturnsOwnershipSynchronously()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact =
                directory.CreateFile(relativePath: "artifact.bin");
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            bool invoked = false;

            Task<SigningOperationResult> resultTask =
                coordinator.ExecuteArtifactAsync(
                    SigningSourceIdentity.Capture(artifact),
                    async () =>
                    {
                        invoked = true;
                        await release.Task;

                        return SigningOperationArtifact.Single(artifact);
                    },
                    out bool ownsOperation);
            Task<SigningOperationResult> waiterTask =
                coordinator.ExecuteArtifactAsync(
                    SigningSourceIdentity.Capture(artifact),
                    () => throw new InvalidOperationException(),
                    out bool waiterOwnsOperation);

            Assert.True(invoked);
            Assert.True(ownsOperation);
            Assert.False(waiterOwnsOperation);
            Assert.False(resultTask.IsCompleted);
            Assert.False(waiterTask.IsCompleted);

            release.SetResult();
            SigningOperationResult[] results =
                await Task.WhenAll(resultTask, waiterTask)
                    .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Same(results[0], results[1]);
        }

        [Fact]
        public async Task TryGetResult_RegisteredIdentity_ReturnsExistingWork()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact =
                directory.CreateFile(relativePath: "artifact.bin");
            SigningSourceIdentity identity =
                SigningSourceIdentity.Capture(artifact);
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int operationCount = 0;

            Task<SigningOperationResult> owner =
                coordinator.ExecuteAsync(
                    identity,
                    async () =>
                    {
                        ++operationCount;
                        await release.Task;

                        return artifact;
                    });

            bool found = coordinator.TryGetResult(
                identity,
                out Task<SigningOperationResult>? existing);

            Assert.True(found);
            Assert.Same(owner, existing);
            Assert.Equal(expected: 1, actual: operationCount);

            release.SetResult();
            SigningOperationResult[] results = await Task.WhenAll(
                    owner,
                    existing!)
                .WaitAsync(timeout: TimeSpan.FromSeconds(value: 5));

            Assert.Same(results[0], results[1]);
            Assert.Equal(expected: 1, actual: operationCount);
        }

        [Fact]
        public async Task TryGetResult_UnregisteredIdentity_ReturnsFalse()
        {
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);

            bool found = coordinator.TryGetResult(
                CreateIdentity(),
                out Task<SigningOperationResult>? resultTask);

            Assert.False(found);
            Assert.Null(resultTask);
        }

        [Fact]
        public async Task TryGetResult_AfterDispose_Throws()
        {
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            await coordinator.DisposeAsync();

            Assert.Throws<ObjectDisposedException>(
                () => coordinator.TryGetResult(
                    CreateIdentity(),
                    out _));
        }

        [Fact]
        public async Task ExecuteAsync_SingleSlotPrerequisite_StartsInline()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo prerequisiteArtifact =
                directory.CreateFile(relativePath: "prerequisite.bin");
            FileInfo dependentArtifact =
                directory.CreateFile(relativePath: "dependent.bin");
            SigningSourceIdentity prerequisiteIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\prerequisite.bin");
            SigningSourceIdentity dependentIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\dependent.bin");
            using SemaphoreSlim slots = new(initialCount: 1, maxCount: 1);
            bool prerequisiteRanWhileSlotHeld = false;

            async Task<FileInfo> RunDependentAsync()
            {
                await slots.WaitAsync();

                try
                {
                    SigningOperationResult prerequisite =
                        await coordinator.WaitAsync(
                            coordinator.ExecuteAsync(
                                prerequisiteIdentity,
                                () =>
                                {
                                    prerequisiteRanWhileSlotHeld =
                                        slots.CurrentCount == 0;

                                    return Task.FromResult(
                                        prerequisiteArtifact);
                                }));

                    FileInfo materialized =
                        prerequisite.Materialize(
                            new FileInfo(
                                Path.Combine(
                                    directory.FullPath,
                                    "materialized",
                                    "prerequisite.bin")));
                    Assert.True(materialized.Exists);

                    return dependentArtifact;
                }
                finally
                {
                    slots.Release();
                }
            }

            Task<SigningOperationResult> resultTask =
                coordinator.ExecuteAsync(
                    dependentIdentity,
                    RunDependentAsync);

            Assert.True(prerequisiteRanWhileSlotHeld);

            SigningOperationResult result =
                await resultTask.WaitAsync(
                    timeout: TimeSpan.FromSeconds(value: 5));
            FileInfo dependent = result.Materialize(
                new FileInfo(
                    Path.Combine(
                        directory.FullPath,
                        "materialized",
                        "dependent.bin")));

            Assert.True(dependent.Exists);
        }

        [Fact]
        public async Task ExecuteAsync_InlinePrerequisiteFailure_ReleasesSingleSlot()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo dependentArtifact =
                directory.CreateFile(relativePath: "dependent.bin");
            SigningSourceIdentity prerequisiteIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\prerequisite.bin");
            SigningSourceIdentity dependentIdentity =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\dependent.bin");
            using SemaphoreSlim slots = new(initialCount: 1, maxCount: 1);
            InvalidOperationException expected = new(message: "Failure.");
            bool dependentPostAwaitRan = false;

            async Task<FileInfo> RunDependentAsync()
            {
                await slots.WaitAsync();

                try
                {
                    await coordinator.WaitAsync(
                        coordinator.ExecuteAsync(
                            prerequisiteIdentity,
                            () => Task.FromException<FileInfo>(expected)));
                    dependentPostAwaitRan = true;

                    return dependentArtifact;
                }
                finally
                {
                    slots.Release();
                }
            }

            InvalidOperationException actual =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => coordinator.ExecuteAsync(
                            dependentIdentity,
                            RunDependentAsync)
                        .WaitAsync(
                            timeout: TimeSpan.FromSeconds(value: 5)));

            Assert.Same(expected, actual);
            Assert.False(dependentPostAwaitRan);
            Assert.Equal(expected: 1, actual: slots.CurrentCount);
        }

        [Fact]
        public async Task ExecuteAsync_ManyConcurrentWaiters_ShareOwnerAndResult()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                relativePath: "signed.bin");
            SigningSourceIdentity identity = CreateIdentity();
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int ownerCount = 0;

            async Task<FileInfo> OperateAsync()
            {
                Interlocked.Increment(ref ownerCount);
                await release.Task;

                return artifact;
            }

            Task<SigningOperationResult>[] waiters = Enumerable.Range(
                    start: 0,
                    count: 100)
                .Select(_ => coordinator.ExecuteAsync(identity, OperateAsync))
                .ToArray();
            release.SetResult();

            SigningOperationResult[] results =
                await Task.WhenAll(waiters).WaitAsync(
                    timeout: TimeSpan.FromSeconds(value: 10));

            Assert.Equal(expected: 1, actual: ownerCount);
            Assert.All(results, result => Assert.Same(results[0], result));
        }

        private static async Task
            AssertDistinctEqualIdentitiesShareOwnerAsync(
                SigningSourceIdentity firstIdentity,
                SigningSourceIdentity secondIdentity)
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                relativePath: "signed.bin");
            int ownerCount = 0;
            bool secondDelegateInvoked = false;

            Assert.NotSame(firstIdentity, secondIdentity);
            Assert.Equal(firstIdentity, secondIdentity);
            Assert.Equal(
                firstIdentity.GetHashCode(),
                secondIdentity.GetHashCode());

            Task<SigningOperationResult> first =
                coordinator.ExecuteAsync(
                    firstIdentity,
                    () =>
                    {
                        ++ownerCount;
                        return Task.FromResult(artifact);
                    });
            Task<SigningOperationResult> second =
                coordinator.ExecuteAsync(
                    secondIdentity,
                    () =>
                    {
                        secondDelegateInvoked = true;
                        return Task.FromResult(artifact);
                    });
            SigningOperationResult[] results =
                await Task.WhenAll(first, second);

            Assert.Equal(expected: 1, actual: ownerCount);
            Assert.False(secondDelegateInvoked);
            Assert.Same(results[0], results[1]);
        }

        [Fact]
        public async Task ExecuteAsync_IdentityOutsideScope_ThrowsWithoutInvokingOperation()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator = new(
                directoryService: directoryService,
                scope: CreateScope(@"C:\publish\App.exe"));
            FileInfo artifact = directory.CreateFile(
                relativePath: "signed.bin");
            int invocationCount = 0;

            Task<FileInfo> OperateAsync()
            {
                ++invocationCount;

                return Task.FromResult(artifact);
            }

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => coordinator.ExecuteAsync(
                        SigningSourceIdentity.PhysicalFile(
                            path: @"C:\publish\App.dll"),
                        OperateAsync));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => coordinator.ExecuteAsync(
                    SigningSourceIdentity.ContainerEntry(
                        parent: SigningSourceIdentity.PhysicalFile(
                            path: @"C:\publish\other.zip"),
                        entryPath: "App.exe"),
                    OperateAsync));

            Assert.Equal(
                "A signing operation requested a file outside its scope.",
                exception.Message);
            Assert.Equal(expected: 0, actual: invocationCount);

            SigningOperationResult result = await coordinator.ExecuteAsync(
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\PUBLISH\app.EXE"),
                OperateAsync);

            Assert.NotNull(result);
            Assert.Equal(expected: 1, actual: invocationCount);
        }

        [Fact]
        public async Task ExecuteAsync_IdentitiesInScope_InvokeOperations()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator = new(
                directoryService: directoryService,
                scope: CreateScope(@"C:\publish\App.application"));
            FileInfo artifact = directory.CreateFile(
                relativePath: "signed.bin");
            SigningSourceIdentity application =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\publish\App.application");
            SigningSourceIdentity[] identities =
            [
                application,
                application.GetSibling(@"Application Files\App.exe"),
                SigningSourceIdentity.ContainerEntry(
                    parent: application.GetSibling("container.zip"),
                    entryPath: "App.exe"),
            ];
            int invocationCount = 0;

            foreach (SigningSourceIdentity identity in identities)
            {
                await coordinator.ExecuteAsync(
                    identity,
                    () =>
                    {
                        ++invocationCount;

                        return Task.FromResult(artifact);
                    });
            }

            Assert.Equal(
                expected: identities.Length,
                actual: invocationCount);
        }

        [Fact]
        public async Task ExecuteAsync_WithoutScope_AcceptsAnyIdentity()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                relativePath: "signed.bin");
            SigningSourceIdentity[] identities =
            [
                SigningSourceIdentity.PhysicalFile(path: @"C:\first.bin"),
                SigningSourceIdentity.PhysicalFile(path: @"D:\second.bin"),
            ];

            foreach (SigningSourceIdentity identity in identities)
            {
                await coordinator.ExecuteAsync(
                    identity,
                    () => Task.FromResult(artifact));
            }
        }

        private static SigningOperationScope CreateScope(string path)
        {
            FileInfo file = new(path);

            return SigningOperationScope.Create(
                new SigningOperationPlan(SigningFile.Capture(file), file),
                requiresSequentialCoordination: candidate => string.Equals(
                    candidate.Extension,
                    ".application",
                    StringComparison.OrdinalIgnoreCase));
        }

        private static SigningSourceIdentity CreateIdentity()
        {
            return SigningSourceIdentity.PhysicalFile(
                path: @"C:\source.bin");
        }

        private sealed class DirectoryServiceSpy : IDirectoryService
        {
            private readonly DirectoryServiceStub _directoryService = new();
            private int _deleteCount;

            internal Exception? DeleteException { get; init; }

            internal int DeleteCount => Volatile.Read(ref _deleteCount);

            internal IReadOnlyList<DirectoryInfo> Directories =>
                _directoryService.Directories;

            public DirectoryInfo CreateTemporaryDirectory()
            {
                return _directoryService.CreateTemporaryDirectory();
            }

            public void Delete(DirectoryInfo directory)
            {
                Interlocked.Increment(ref _deleteCount);

                if (DeleteException is not null)
                {
                    throw DeleteException;
                }

                _directoryService.Delete(directory);
            }

            public void Dispose()
            {
                _directoryService.Dispose();
            }
        }
    }
}
