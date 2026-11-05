// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class SigningOperationCoordinatorSchedulingTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public async Task Scheduling_IndependentOwners_BoundsWorkAndClaimsSynchronously(
            int maxConcurrency)
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            using IDisposable scheduling = coordinator.UseScheduling(maxConcurrency);
            FileInfo artifact = directory.CreateFile("signed.bin");
            TaskCompletionSource filled = Signal();
            TaskCompletionSource release = Signal();
            int active = 0;
            int entered = 0;
            List<Task<SigningOperationResult>> owners = new();

            for (int i = 0; i < 12; ++i)
            {
                owners.Add(coordinator.ExecuteArtifactAsync(
                    Identity(i),
                    async () =>
                    {
                        int count = Interlocked.Increment(ref active);
                        Assert.InRange(count, 1, maxConcurrency);
                        if (Interlocked.Increment(ref entered) == maxConcurrency)
                        {
                            filled.SetResult();
                        }

                        await release.Task;
                        Interlocked.Decrement(ref active);
                        return SigningOperationArtifact.Single(artifact);
                    },
                    out bool ownsOperation));
                Assert.True(ownsOperation);
            }

            _ = coordinator.ExecuteArtifactAsync(
                Identity(11),
                () => throw new InvalidOperationException(),
                out bool duplicateOwnsOperation);
            Assert.False(duplicateOwnsOperation);

            try
            {
                await filled.Task.WaitAsync(Timeout);
                Assert.Equal(maxConcurrency, Volatile.Read(ref active));
                Assert.Equal(maxConcurrency, Volatile.Read(ref entered));
            }
            finally
            {
                release.TrySetResult();
            }

            await Task.WhenAll(owners).WaitAsync(Timeout);
            Assert.Equal(12, entered);
            Assert.Equal(0, active);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(4, false)]
        [InlineData(1, true)]
        [InlineData(4, true)]
        public async Task Scheduling_ReversePrerequisiteChain_Completes(
            int maxConcurrency,
            bool explicitOwners)
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            using IDisposable scheduling = coordinator.UseScheduling(maxConcurrency);
            FileInfo artifact = directory.CreateFile("signed.bin");
            TaskCompletionSource start = Signal();
            Task<SigningOperationResult>[] owners =
                new Task<SigningOperationResult>[12];
            int operationCount = 0;
            int runnable = 0;

            async Task<FileInfo> RunAsync(int index)
            {
                Interlocked.Increment(ref operationCount);
                int count = Interlocked.Increment(ref runnable);
                Assert.InRange(count, 1, maxConcurrency);
                await start.Task;

                if (index < owners.Length - 1)
                {
                    Task<SigningOperationResult> prerequisite = explicitOwners
                        ? owners[index + 1]
                        : coordinator.ExecuteAsync(
                            Identity(index + 1), () => RunAsync(index + 1));
                    Interlocked.Decrement(ref runnable);
                    await coordinator.WaitForReadyOwnerAsync(prerequisite);
                    count = Interlocked.Increment(ref runnable);
                    Assert.InRange(count, 1, maxConcurrency);
                }

                await Task.Yield();
                Interlocked.Decrement(ref runnable);
                return artifact;
            }

            if (explicitOwners)
            {
                for (int i = 0; i < owners.Length; ++i)
                {
                    int index = i;
                    owners[i] = coordinator.ExecuteAsync(
                        Identity(index), () => RunAsync(index));
                }
            }
            else
            {
                owners[0] = coordinator.ExecuteAsync(Identity(0), () => RunAsync(0));
            }

            start.SetResult();
            Task completion = explicitOwners ? Task.WhenAll(owners) : owners[0];
            await completion.WaitAsync(Timeout);
            Assert.Equal(12, operationCount);
            Assert.Equal(0, runnable);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public async Task Scheduling_AggregateWait_WithNestedOrdinaryChild_Completes(
            int maxConcurrency)
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            using IDisposable scheduling = coordinator.UseScheduling(maxConcurrency);
            FileInfo artifact = directory.CreateFile("signed.bin");
            Task<SigningOperationResult>? second = null;
            Task<SigningOperationResult> owner = coordinator.ExecuteAsync(
                Identity(0),
                async () =>
                {
                    Task<SigningOperationResult> first = coordinator.ExecuteAsync(
                        Identity(1), async () =>
                        {
                            await Task.Yield();
                            return artifact;
                        });
                    second = coordinator.ExecuteAsync(
                        Identity(2), async () =>
                        {
                            await coordinator.WaitForReadyOwnerAsync(
                                coordinator.ExecuteAsync(
                                    Identity(3), () => Task.FromResult(artifact)));
                            return artifact;
                        });

                    SigningOperationResult[] results =
                        await coordinator.WaitAllForReadyOwnerAsync(
                            new[] { first, second });
                    Assert.Equal(2, results.Length);

                    return artifact;
                });

            await owner.WaitAsync(Timeout);
            await second!.WaitAsync(Timeout);
        }

        [Theory]
        [InlineData(1, false, false)]
        [InlineData(4, false, false)]
        [InlineData(1, true, false)]
        [InlineData(4, true, false)]
        [InlineData(1, false, true)]
        [InlineData(4, false, true)]
        [InlineData(1, true, true)]
        [InlineData(4, true, true)]
        public async Task Scheduling_OwnedMilestone_PreservesCycleAndProgressBoundaries(
            int maxConcurrency,
            bool reverseFirst,
            bool cycle)
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            using IDisposable scheduling = coordinator.UseScheduling(maxConcurrency);
            FileInfo artifact = directory.CreateFile("signed.bin");
            TaskCompletionSource begin = Signal();
            TaskCompletionSource waitRegistered = Signal();
            TaskCompletionSource reverseRegistered = Signal();
            TaskCompletionSource milestone = Signal();
            Task<SigningOperationResult>? owner = null;
            Task<SigningOperationResult>? reverse = null;

            async Task<FileInfo> RunOwnerAsync()
            {
                await begin.Task;
                if (reverseFirst)
                {
                    await reverseRegistered.Task;
                }

                Task<Task> wait;
                try
                {
                    wait = coordinator.WaitForReadyOwnerAsync(
                        reverse!, milestone.Task);
                }
                finally
                {
                    waitRegistered.TrySetResult();
                }

                Task winner = await wait;
                await winner;
                return artifact;
            }

            async Task<FileInfo> RunReverseAsync()
            {
                await begin.Task;
                if (!reverseFirst)
                {
                    await waitRegistered.Task;
                }

                if (!cycle)
                {
                    milestone.SetResult();
                }

                Task<SigningOperationResult> wait;
                try
                {
                    wait = coordinator.WaitForReadyOwnerAsync(owner!);
                }
                finally
                {
                    reverseRegistered.TrySetResult();
                }

                await wait;
                return artifact;
            }

            if (reverseFirst)
            {
                reverse = coordinator.ExecuteAsync(Identity(1), RunReverseAsync);
                owner = coordinator.ExecuteAsync(Identity(0), RunOwnerAsync);
            }
            else
            {
                owner = coordinator.ExecuteAsync(Identity(0), RunOwnerAsync);
                reverse = coordinator.ExecuteAsync(Identity(1), RunReverseAsync);
            }

            try
            {
                begin.SetResult();
                await waitRegistered.Task.WaitAsync(Timeout);
                await reverseRegistered.Task.WaitAsync(Timeout);

                if (cycle)
                {
                    InvalidOperationException failure =
                        await Assert.ThrowsAsync<InvalidOperationException>(
                            () => owner.WaitAsync(Timeout));
                    Assert.Equal(
                        "A signing operation dependency cycle was detected.",
                        failure.Message);
                    Assert.Same(failure,
                        await Assert.ThrowsAsync<InvalidOperationException>(
                            () => reverse.WaitAsync(Timeout)));
                }
                else
                {
                    await Task.WhenAll(owner, reverse).WaitAsync(Timeout);
                    Assert.True(milestone.Task.IsCompletedSuccessfully);
                }
            }
            finally
            {
                milestone.TrySetResult();
            }

            await coordinator.ExecuteAsync(Identity(3), () => Task.FromResult(artifact))
                .WaitAsync(Timeout);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public async Task Scheduling_MilestoneBoundary_AllowsReverseWait(
            int maxConcurrency)
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            using IDisposable scheduling = coordinator.UseScheduling(maxConcurrency);
            FileInfo artifact = directory.CreateFile("signed.bin");
            TaskCompletionSource start = Signal();
            TaskCompletionSource milestone = Signal();
            Task<SigningOperationResult>? owner = null;
            Task<SigningOperationResult>? child = null;
            owner = coordinator.ExecuteAsync(Identity(0), async () =>
            {
                await start.Task;
                child = coordinator.ExecuteAsync(Identity(1), async () =>
                {
                    milestone.SetResult();
                    await coordinator.WaitForReadyOwnerAsync(owner!);
                    return artifact;
                });
                Task winner = await coordinator.WaitForReadyOwnerAsync(
                    child, milestone.Task);
                Assert.Same(milestone.Task, winner);
                return artifact;
            });
            start.SetResult();
            await owner.WaitAsync(Timeout);
            await child!.WaitAsync(Timeout);
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(4, 0)]
        [InlineData(1, 1)]
        [InlineData(4, 1)]
        public async Task Scheduling_Cycle_FaultsWithSameExceptionAndReusesPermit(
            int maxConcurrency,
            int waitKind)
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            using IDisposable scheduling = coordinator.UseScheduling(maxConcurrency);
            FileInfo artifact = directory.CreateFile("signed.bin");
            TaskCompletionSource start = Signal();
            TaskCompletionSource milestone = Signal();
            Task<SigningOperationResult>? owner = null;
            Task<SigningOperationResult>? child = null;
            owner = coordinator.ExecuteAsync(Identity(0), async () =>
            {
                await start.Task;
                child = coordinator.ExecuteAsync(Identity(1), async () =>
                {
                    await coordinator.WaitForReadyOwnerAsync(owner!);
                    return artifact;
                });
                if (waitKind == 0)
                {
                    await coordinator.WaitAllForReadyOwnerAsync(new[] { child });
                }
                else
                {
                    Task winner = await coordinator.WaitForReadyOwnerAsync(
                        child, milestone.Task);
                    await winner;
                }
                return artifact;
            });
            start.SetResult();
            InvalidOperationException ownerException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => owner.WaitAsync(Timeout));
            InvalidOperationException childException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => child!.WaitAsync(Timeout));
            Assert.Same(ownerException, childException);
            Assert.Equal(
                "A signing operation dependency cycle was detected.",
                ownerException.Message);
            await coordinator.ExecuteAsync(Identity(2), () => Task.FromResult(artifact))
                .WaitAsync(Timeout);
        }

        [Fact]
        public async Task Scheduling_OverlappingAwareWaits_RejectWithoutStrandingPermit()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            FileInfo artifact = directory.CreateFile("signed.bin");
            TaskCompletionSource release = Signal();
            Task<SigningOperationResult> prerequisite = coordinator.ExecuteAsync(
                Identity(1), async () =>
                {
                    await release.Task;
                    return artifact;
                });
            using IDisposable scheduling = coordinator.UseScheduling(1);
            TaskCompletionSource registered = Signal();
            Task<SigningOperationResult> owner = coordinator.ExecuteAsync(
                Identity(0), async () =>
                {
                    Task<SigningOperationResult> wait =
                        coordinator.WaitForReadyOwnerAsync(prerequisite);
                    Assert.Throws<InvalidOperationException>(
                        () => { _ = coordinator.WaitForReadyOwnerAsync(prerequisite); });
                    registered.SetResult();
                    await wait;
                    return artifact;
                });
            try
            {
                await registered.Task.WaitAsync(Timeout);
            }
            finally
            {
                release.TrySetResult();
            }

            await owner.WaitAsync(Timeout);
            await coordinator.ExecuteAsync(Identity(2), () => Task.FromResult(artifact))
                .WaitAsync(Timeout);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(4, false)]
        [InlineData(1, true)]
        [InlineData(4, true)]
        public async Task Scheduling_AwareWaitFault_ReacquiresBeforeOwnerRecovery(
            int maxConcurrency,
            bool waitAll)
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            using IDisposable scheduling = coordinator.UseScheduling(maxConcurrency);
            FileInfo artifact = directory.CreateFile("signed.bin");
            OperationCanceledException expected = new("prerequisite failure");
            Task<SigningOperationResult> owner = coordinator.ExecuteAsync(
                Identity(0), async () =>
                {
                    Task<SigningOperationResult> prerequisite =
                        coordinator.ExecuteAsync(
                            Identity(1),
                            () => Task.FromException<FileInfo>(expected));
                    Task<SigningOperationResult> second = coordinator.ExecuteAsync(
                        Identity(3), async () =>
                        {
                            await coordinator.WaitForReadyOwnerAsync(
                                coordinator.ExecuteAsync(
                                    Identity(4), () => Task.FromResult(artifact)));
                            return artifact;
                        });

                    if (waitAll)
                    {
                        Assert.Same(expected,
                            await Assert.ThrowsAsync<OperationCanceledException>(
                                () => coordinator.WaitAllForReadyOwnerAsync(
                                    new[] { prerequisite, second })));
                        Assert.True(second.IsCompletedSuccessfully);
                    }
                    else
                    {
                        Assert.Same(expected,
                            await Assert.ThrowsAsync<OperationCanceledException>(
                                () => coordinator.WaitForReadyOwnerAsync(prerequisite)));
                    }

                    await coordinator.WaitForReadyOwnerAsync(
                        coordinator.ExecuteAsync(
                            Identity(2), () => Task.FromResult(artifact)));
                    await coordinator.WaitForReadyOwnerAsync(second);
                    return artifact;
                });
            await owner.WaitAsync(Timeout);
        }

        [Fact]
        public async Task Scheduling_GroupRegistrationFailure_RollsBackBeforeSuspension()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            using IDisposable scheduling = coordinator.UseScheduling(1);
            FileInfo artifact = directory.CreateFile("signed.bin");
            Task<SigningOperationResult> foreign =
                new TaskCompletionSource<SigningOperationResult>().Task;
            Task<SigningOperationResult> owner = coordinator.ExecuteAsync(
                Identity(0), async () =>
                {
                    Task<SigningOperationResult> prerequisite =
                        coordinator.ExecuteAsync(
                            Identity(1), () => Task.FromResult(artifact));
                    Assert.Throws<ArgumentException>(
                        () => { _ = coordinator.WaitAllForReadyOwnerAsync(
                            new[] { prerequisite, foreign }); });
                    await coordinator.WaitAllForReadyOwnerAsync(
                        new[] { prerequisite, prerequisite });
                    Assert.Empty(await coordinator.WaitAllForReadyOwnerAsync(
                        Array.Empty<Task<SigningOperationResult>>()));
                    return artifact;
                });
            await owner.WaitAsync(Timeout);
        }

        [Fact]
        public async Task Scheduling_UnawaitedAwareWait_RetainsCleanupOwnership()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            using IDisposable scheduling = coordinator.UseScheduling(1);
            FileInfo artifact = directory.CreateFile("signed.bin");
            Task<SigningOperationResult>? wait = null;
            int cleanups = 0;
            Task<SigningOperationResult> owner = coordinator.ExecuteArtifactAsync(
                Identity(0), () =>
                {
                    wait = coordinator.WaitForReadyOwnerAsync(
                        coordinator.ExecuteAsync(
                            Identity(1), () => Task.FromResult(artifact)));
                    return Task.FromResult(SigningOperationArtifact.OwnedSingle(
                        artifact, new CallbackDisposable(() => ++cleanups)));
                });
            await owner.WaitAsync(Timeout);
            await wait!.WaitAsync(Timeout);
            Assert.Equal(1, cleanups);
            await coordinator.ExecuteAsync(Identity(2), () => Task.FromResult(artifact))
                .WaitAsync(Timeout);
        }

        [Fact]
        public async Task Scheduling_ScopeFlowsToQueuedNestedWorkButNotUnrelatedInvocations()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            FileInfo artifact = directory.CreateFile("signed.bin");
            TaskCompletionSource release = Signal();
            Task<SigningOperationResult> queued;
            Task<SigningOperationResult> blocker;
            bool unrelatedInvoked = false;
            Assert.False(coordinator.IsSchedulingEnabled);

            using (coordinator.UseScheduling(1))
            {
                Assert.True(coordinator.IsSchedulingEnabled);
                blocker = coordinator.ExecuteAsync(Identity(0), async () =>
                {
                    await release.Task;
                    return artifact;
                });
                queued = coordinator.ExecuteAsync(Identity(1), async () =>
                {
                    Assert.True(coordinator.IsSchedulingEnabled);
                    await coordinator.WaitForReadyOwnerAsync(
                        coordinator.ExecuteAsync(
                            Identity(2), () => Task.FromResult(artifact)));
                    return artifact;
                });
            }

            Assert.False(coordinator.IsSchedulingEnabled);
            Task<SigningOperationResult> unrelated = coordinator.ExecuteAsync(
                Identity(3), () =>
                {
                    Assert.False(coordinator.IsSchedulingEnabled);
                    unrelatedInvoked = true;
                    return Task.FromResult(artifact);
                });
            Assert.True(unrelatedInvoked);
            release.SetResult();
            await Task.WhenAll(blocker, queued, unrelated).WaitAsync(Timeout);
        }

        [Fact]
        public async Task Scheduling_DoesNotFlowToAnotherCoordinator()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub firstService = new();
            using DirectoryServiceStub secondService = new();
            await using SigningOperationCoordinator first = new(firstService);
            await using SigningOperationCoordinator second = new(secondService);
            using IDisposable scheduling = first.UseScheduling(1);
            Assert.True(first.IsSchedulingEnabled);
            Assert.False(second.IsSchedulingEnabled);
            FileInfo artifact = directory.CreateFile("signed.bin");
            TaskCompletionSource release = Signal();
            Task<SigningOperationResult> blocker = first.ExecuteAsync(
                Identity(0), async () =>
                {
                    await release.Task;
                    return artifact;
                });
            bool invoked = false;
            Task<SigningOperationResult> unrelated = second.ExecuteAsync(
                Identity(1), () =>
                {
                    invoked = true;
                    return Task.FromResult(artifact);
                });
            try
            {
                Assert.True(invoked);
            }
            finally
            {
                release.TrySetResult();
            }

            await Task.WhenAll(blocker, unrelated).WaitAsync(Timeout);
        }

        [Fact]
        public async Task Scheduling_CapturedCompletedOwner_DoesNotReacquireAnOrphanedPermit()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            using IDisposable scheduling = coordinator.UseScheduling(1);
            FileInfo artifact = directory.CreateFile("signed.bin");
            TaskCompletionSource resumeCaptured = Signal();
            TaskCompletionSource registered = Signal();
            TaskCompletionSource releaseChild = Signal();
            Task? captured = null;
            await coordinator.ExecuteAsync(Identity(0), () =>
            {
                captured = Task.Run(async () =>
                {
                    await resumeCaptured.Task;
                    Task<SigningOperationResult> wait =
                        coordinator.WaitForReadyOwnerAsync(
                            coordinator.ExecuteAsync(Identity(1), async () =>
                            {
                                await releaseChild.Task;
                                return artifact;
                            }));
                    registered.SetResult();
                    await wait;
                });
                return Task.FromResult(artifact);
            }).WaitAsync(Timeout);

            try
            {
                resumeCaptured.SetResult();
                await registered.Task.WaitAsync(Timeout);
            }
            finally
            {
                resumeCaptured.TrySetResult();
                releaseChild.TrySetResult();
            }

            await captured!.WaitAsync(Timeout);
            await coordinator.ExecuteAsync(Identity(2), () => Task.FromResult(artifact))
                .WaitAsync(Timeout);
        }

        [Fact]
        public async Task ReadyOwnerSingleWait_Unscheduled_PreservesBorrowedFaultTask()
        {
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            OperationCanceledException expected = new("operation failure");
            Task<SigningOperationResult> fault = coordinator.ExecuteAsync(
                Identity(0), () => Task.FromException<FileInfo>(expected));
            Task<SigningOperationResult> wait =
                coordinator.WaitForReadyOwnerAsync(fault);
            Assert.Same(fault, wait);
            Assert.Same(expected, await Assert.ThrowsAsync<OperationCanceledException>(
                () => wait));
            Assert.True(wait.IsFaulted);
            Assert.False(wait.IsCanceled);
        }

        [Fact]
        public async Task Scheduling_CleanupOwnsPermitAndCompletion_DisposalWaits()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            using IDisposable scheduling = coordinator.UseScheduling(1);
            FileInfo artifact = directory.CreateFile("signed.bin");
            TaskCompletionSource cleanupEntered = Signal();
            TaskCompletionSource releaseCleanup = Signal();
            int cleanupCount = 0;
            bool secondInvoked = false;
            Task<SigningOperationResult> first = Task.Run(() =>
                coordinator.ExecuteArtifactAsync(Identity(0), () =>
                    Task.FromResult(SigningOperationArtifact.OwnedSingle(
                        artifact, new CallbackDisposable(() =>
                        {
                            Interlocked.Increment(ref cleanupCount);
                            Assert.Single(Assert.Single(service.Directories).EnumerateFiles());
                            cleanupEntered.SetResult();
                            releaseCleanup.Task.GetAwaiter().GetResult();
                        })))));
            await cleanupEntered.Task.WaitAsync(Timeout);
            Task<SigningOperationResult> second = coordinator.ExecuteAsync(
                Identity(1), () =>
                {
                    secondInvoked = true;
                    return Task.FromResult(artifact);
                });
            Task disposal = coordinator.DisposeAsync().AsTask();

            try
            {
                Assert.False(first.IsCompleted);
                Assert.False(secondInvoked);
                Assert.False(disposal.IsCompleted);
            }
            finally
            {
                releaseCleanup.TrySetResult();
            }

            await Task.WhenAll(first, second).WaitAsync(Timeout);
            await disposal.WaitAsync(Timeout);
            Assert.Equal(1, cleanupCount);
            Assert.True(secondInvoked);
        }

        [Fact]
        public async Task Scheduling_SnapshotAndCleanupFaults_ReusePermitAndShareCancellationException()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            using IDisposable scheduling = coordinator.UseScheduling(1);
            FileInfo artifact = directory.CreateFile("signed.bin");
            FileInfo missing = new(Path.Combine(directory.FullPath, "missing.bin"));
            int cleanupCount = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                coordinator.ExecuteArtifactAsync(Identity(0), () =>
                    Task.FromResult(SigningOperationArtifact.OwnedSingle(
                        missing, new CallbackDisposable(() => ++cleanupCount))))
                    .WaitAsync(Timeout));
            OperationCanceledException expected = new("cleanup failure");
            Task<SigningOperationResult> fault = coordinator.ExecuteArtifactAsync(
                Identity(1), () => Task.FromResult(
                    SigningOperationArtifact.OwnedSingle(
                        artifact, new CallbackDisposable(() => throw expected))));
            Task<SigningOperationResult> duplicate = coordinator.ExecuteAsync(
                Identity(1), () => throw new InvalidOperationException());
            Assert.Same(expected, await Assert.ThrowsAsync<OperationCanceledException>(
                () => fault.WaitAsync(Timeout)));
            Assert.Same(expected, await Assert.ThrowsAsync<OperationCanceledException>(
                () => duplicate.WaitAsync(Timeout)));
            Assert.True(fault.IsFaulted);
            Assert.False(fault.IsCanceled);
            Assert.Equal(1, cleanupCount);
            await coordinator.ExecuteAsync(Identity(2), () => Task.FromResult(artifact))
                .WaitAsync(Timeout);
        }

        [Fact]
        public async Task Scheduling_ExternalSchedulingLease_IsBoundedIdempotentAndAwaitedByDisposal()
        {
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            using IDisposable scheduling = coordinator.UseScheduling(1);
            IDisposable first = await coordinator.AcquireSchedulingLeaseAsync();
            Task<IDisposable> secondTask = coordinator.AcquireSchedulingLeaseAsync();
            Task disposal = coordinator.DisposeAsync().AsTask();
            Assert.False(secondTask.IsCompleted);
            Assert.False(disposal.IsCompleted);
            first.Dispose();
            first.Dispose();
            IDisposable second = await secondTask.WaitAsync(Timeout);
            Assert.False(disposal.IsCompleted);
            second.Dispose();
            await disposal.WaitAsync(Timeout);
            Assert.Throws<ObjectDisposedException>(
                () => { _ = coordinator.AcquireSchedulingLeaseAsync(); });
        }

        [Fact]
        public async Task Scheduling_ExternalLeaseInsideOwner_IsRejectedWithoutBlocking()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub service = new();
            await using SigningOperationCoordinator coordinator = new(service);
            using IDisposable scheduling = coordinator.UseScheduling(1);
            FileInfo artifact = directory.CreateFile("signed.bin");
            await coordinator.ExecuteAsync(Identity(0), () =>
            {
                Assert.Throws<InvalidOperationException>(() =>
                {
                    _ = coordinator.AcquireSchedulingLeaseAsync();
                });
                return Task.FromResult(artifact);
            }).WaitAsync(Timeout);
            using IDisposable lease =
                await coordinator.AcquireSchedulingLeaseAsync().WaitAsync(Timeout);
        }

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private static TaskCompletionSource Signal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static SigningSourceIdentity Identity(int index) =>
            SigningSourceIdentity.PhysicalFile(
                Path.Combine(@"C:\ready-owner-tests", $"{index}.bin"));

        private sealed class CallbackDisposable : IDisposable
        {
            private readonly Action _callback;

            internal CallbackDisposable(Action callback) => _callback = callback;

            public void Dispose() => _callback();
        }
    }
}
