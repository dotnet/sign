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
            using SigningOperationCoordinator coordinator =
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
            using SigningOperationCoordinator coordinator =
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
            using SigningOperationCoordinator coordinator =
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
            using SigningOperationCoordinator coordinator =
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
        public async Task ExecuteAsync_SharedArtifactIsUnavailable_SharesException()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            using SigningOperationCoordinator coordinator =
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
            using SigningOperationCoordinator coordinator =
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
            using SigningOperationCoordinator firstCoordinator =
                new(directoryService: directoryService);
            using SigningOperationCoordinator secondCoordinator =
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
            using SigningOperationCoordinator firstCoordinator =
                new(directoryService: directoryService);
            using SigningOperationCoordinator secondCoordinator =
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
            using SigningOperationCoordinator coordinator =
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
            using SigningOperationCoordinator coordinator =
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
            using SigningOperationCoordinator coordinator =
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
            using SigningOperationCoordinator coordinator =
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
            using SigningOperationCoordinator coordinator =
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
            using SigningOperationCoordinator coordinator =
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
        public async Task ExecuteAsync_OwnerArtifactDeleted_ResultStillMaterializes()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            using SigningOperationCoordinator coordinator =
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
            using SigningOperationCoordinator coordinator =
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
            using SigningOperationCoordinator coordinator =
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
        public async Task Dispose_ExistingSnapshots_DeletesSnapshotsAndPreventsMaterialization()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            SigningOperationCoordinator coordinator = new(
                directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                "signed.bin",
                "signed content");
            SigningOperationResult result = await coordinator.ExecuteAsync(
                CreateIdentity(),
                () => Task.FromResult(artifact));
            DirectoryInfo snapshotDirectory =
                Assert.Single(directoryService.Directories);

            Assert.True(snapshotDirectory.Exists);

            coordinator.Dispose();
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
        public void ExecuteAsync_AfterDispose_Throws()
        {
            using DirectoryServiceStub directoryService = new();
            SigningOperationCoordinator coordinator = new(
                directoryService: directoryService);
            coordinator.Dispose();

            Assert.Throws<ObjectDisposedException>(
                () =>
                {
                    _ = coordinator.ExecuteAsync(
                        CreateIdentity(),
                        () => throw new InvalidOperationException());
                });
        }

        [Fact]
        public void Dispose_ActiveOperation_DefersCleanupUntilLeaseReleased()
        {
            bool cleanedUp = false;
            using SigningOperationSnapshotLifetime lifetime = new(
                cleanup: () => cleanedUp = true);
            using IDisposable lease =
                lifetime.EnterOperation();

            lifetime.Dispose();

            Assert.False(cleanedUp);
            Assert.Throws<ObjectDisposedException>(
                () => lifetime.EnterOperation());

            lease.Dispose();

            Assert.True(cleanedUp);
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
        public async Task ExecuteAsync_DirectSelfAwaitAfterAwait_Throws()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile(
                relativePath: "signed.bin");
            SigningSourceIdentity identity = CreateIdentity();
            bool nestedOperationRan = false;

            async Task<FileInfo> OperateAsync()
            {
                await Task.Yield();
                await coordinator.ExecuteAsync(
                    identity,
                    () =>
                    {
                        nestedOperationRan = true;
                        return Task.FromResult(artifact);
                    });

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
        public async Task ExecuteAsync_CompletedOperationFromCapturedContext_ReusesResult()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            using SigningOperationCoordinator coordinator =
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
            using SigningOperationCoordinator coordinator =
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
        public async Task ExecuteAsync_SingleSlotPrerequisite_StartsInline()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            using SigningOperationCoordinator coordinator =
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
                        await coordinator.ExecuteAsync(
                            prerequisiteIdentity,
                            () =>
                            {
                                prerequisiteRanWhileSlotHeld =
                                    slots.CurrentCount == 0;

                                return Task.FromResult(
                                    prerequisiteArtifact);
                            });

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
            using SigningOperationCoordinator coordinator =
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
                    await coordinator.ExecuteAsync(
                        prerequisiteIdentity,
                        () => Task.FromException<FileInfo>(expected));
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
            using SigningOperationCoordinator coordinator =
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
            using SigningOperationCoordinator coordinator =
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

        private static SigningSourceIdentity CreateIdentity()
        {
            return SigningSourceIdentity.PhysicalFile(
                path: @"C:\source.bin");
        }

    }
}
