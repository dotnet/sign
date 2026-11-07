// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class SigningOperationCoordinatorWaitOverlapTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task WaitAsync_OverlappingRegistrations_PreserveAggregateSemantics(
            bool awaitAny,
            bool useMilestones)
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile("signed.bin");
            TaskCompletionSource firstRelease = NewSignal();
            TaskCompletionSource secondRelease = NewSignal();
            TaskCompletionSource firstMilestone = NewSignal();
            TaskCompletionSource secondMilestone = NewSignal();
            TaskCompletionSource resumed = NewSignal();
            Task? firstWait = null;
            Task? secondWait = null;
            Task<SigningOperationResult>? secondChild = null;

            Task<SigningOperationResult> owner = coordinator.ExecuteAsync(
                Identity("owner"),
                async () =>
                {
                    Task<SigningOperationResult> firstChild =
                        coordinator.ExecuteAsync(
                            Identity("first"),
                            async () =>
                            {
                                await firstRelease.Task;
                                firstMilestone.SetResult();
                                return artifact;
                            });
                    secondChild = coordinator.ExecuteAsync(
                        Identity("second"),
                        async () =>
                        {
                            await secondRelease.Task;
                            secondMilestone.SetResult();
                            return artifact;
                        });
                    firstWait = useMilestones
                        ? coordinator.WaitAsync(firstChild, firstMilestone.Task)
                        : coordinator.WaitAsync(firstChild);
                    secondWait = useMilestones
                        ? coordinator.WaitAsync(secondChild, secondMilestone.Task)
                        : coordinator.WaitAsync(secondChild);

                    if (awaitAny)
                    {
                        await Task.WhenAny(firstWait, secondWait);
                    }
                    else
                    {
                        await Task.WhenAll(firstWait, secondWait);
                    }

                    resumed.SetResult();
                    return artifact;
                });

            try
            {
                Assert.NotNull(firstWait);
                Assert.NotNull(secondWait);
                Assert.NotNull(secondChild);
                firstRelease.SetResult();
                await firstWait.WaitAsync(TimeSpan.FromSeconds(5));

                if (awaitAny)
                {
                    await resumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await owner.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.False(secondChild.IsCompleted);
                    Assert.False(secondWait.IsCompleted);
                }
                else
                {
                    Assert.False(resumed.Task.IsCompleted);
                    Assert.False(owner.IsCompleted);
                }
            }
            finally
            {
                firstRelease.TrySetResult();
                secondRelease.TrySetResult();
            }

            await owner.WaitAsync(TimeSpan.FromSeconds(5));
            await secondWait!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(resumed.Task.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task WaitAsync_WhenAnyResumes_UnfinishedRegistrationStillDetectsCycle()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService: directoryService);
            FileInfo artifact = directory.CreateFile("signed.bin");
            TaskCompletionSource firstRelease = NewSignal();
            TaskCompletionSource reverseWait = NewSignal();
            TaskCompletionSource resumed = NewSignal();
            TaskCompletionSource finishOwner = NewSignal();
            Task<SigningOperationResult>? owner = null;
            Task<SigningOperationResult>? secondChild = null;
            Task<SigningOperationResult>? secondWait = null;

            owner = coordinator.ExecuteAsync(
                Identity("owner"),
                async () =>
                {
                    Task<SigningOperationResult> firstChild =
                        coordinator.ExecuteAsync(
                            Identity("first"),
                            async () =>
                            {
                                await firstRelease.Task;
                                return artifact;
                            });
                    secondChild = coordinator.ExecuteAsync(
                        Identity("second"),
                        async () =>
                        {
                            await reverseWait.Task;
                            await coordinator.WaitAsync(owner!);
                            return artifact;
                        });
                    Task<SigningOperationResult> firstWait =
                        coordinator.WaitAsync(firstChild);
                    secondWait = coordinator.WaitAsync(secondChild);
                    await Task.WhenAny(firstWait, secondWait);
                    resumed.SetResult();
                    await finishOwner.Task;
                    return artifact;
                });

            try
            {
                firstRelease.SetResult();
                await resumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                reverseWait.SetResult();
                InvalidOperationException childException =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => secondChild!.WaitAsync(TimeSpan.FromSeconds(5)));
                InvalidOperationException waitException =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => secondWait!.WaitAsync(TimeSpan.FromSeconds(5)));

                Assert.Equal(
                    "A signing operation dependency cycle was detected.",
                    childException.Message);
                Assert.Same(childException, waitException);
            }
            finally
            {
                firstRelease.TrySetResult();
                reverseWait.TrySetResult();
                finishOwner.TrySetResult();
            }

            await owner.WaitAsync(TimeSpan.FromSeconds(5));
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static SigningSourceIdentity Identity(string name) =>
            SigningSourceIdentity.PhysicalFile(
                Path.Combine(@"C:\coordinator-wait-overlap", name + ".bin"));
    }
}
