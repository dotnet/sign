// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Reflection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Sign.Core.Test
{
    public sealed class SigningOperationGroupExecutionTests : IDisposable
    {
        private readonly DirectoryService _directoryService;
        private readonly TemporaryDirectory _temporaryDirectory;

        public SigningOperationGroupExecutionTests()
        {
            _directoryService = new DirectoryService(
                Substitute.For<ILogger<IDirectoryService>>());
            _temporaryDirectory = new TemporaryDirectory(
                _directoryService);
        }

        public void Dispose()
        {
            _temporaryDirectory.Dispose();
            _directoryService.Dispose();
        }

        [Fact]
        public async Task CompletePlanAsync_WhenScopeCompletionOverlapsDisposal_CompletesScopeBeforeDisposal()
        {
            SigningOperationPlan first = CreatePlan("First.dll");
            SigningOperationPlan second = CreatePlan("Second.dll");
            SigningOperationScope firstScope =
                SigningOperationScope.Create(
                    first,
                    requiresSequentialCoordination: _ => false);
            SigningOperationScope secondScope =
                SigningOperationScope.Create(
                    second,
                    requiresSequentialCoordination: _ => false);
            SigningOperationGroup group = new(
                new[] { first, second },
                SigningOperationScope.Create(
                    new[] { first, second },
                    requiresSequentialCoordination: _ => false),
                new Dictionary<
                    SigningOperationPlan,
                    SigningOperationScope>
                {
                    [first] = firstScope,
                    [second] = secondScope,
                });
            Signer.SigningOperationGroupExecution execution = new(
                group,
                Substitute.For<IAggregatingDataFormatSigner>(),
                _directoryService,
                Substitute.For<ILogger<ISigner>>());
            SigningOperationCoordinator coordinator = new(
                _directoryService,
                group.Scope,
                group.PlanScopes);
            FieldInfo coordinatorField = GetField(
                typeof(Signer.SigningOperationGroupExecution),
                "_coordinator");
            FieldInfo groupGateField = GetField(
                typeof(Signer.SigningOperationGroupExecution),
                "_gate");
            FieldInfo remainingPlanCountField = GetField(
                typeof(Signer.SigningOperationGroupExecution),
                "_remainingPlanCount");
            FieldInfo coordinatorGateField = GetField(
                typeof(SigningOperationCoordinator),
                "_gate");
            object groupGate = groupGateField.GetValue(execution)!;
            object coordinatorGate =
                coordinatorGateField.GetValue(coordinator)!;
            Task? secondCompletion = null;
            bool completedSecondWhileCoordinatorBlocked = false;

            coordinatorField.SetValue(execution, coordinator);
            Monitor.Enter(coordinatorGate);

            Task firstCompletion;

            try
            {
                firstCompletion = Task.Run(
                    () => execution.CompletePlanAsync(
                        first,
                        DisposeAsync));
                Assert.True(
                    SpinWait.SpinUntil(
                        () =>
                            (int)remainingPlanCountField.GetValue(
                                execution)! == 1,
                        TimeSpan.FromSeconds(5)));

                // The old implementation released the group gate before
                // completing the scope, allowing the last plan to dispose.
                if (Monitor.TryEnter(
                    groupGate,
                    TimeSpan.FromMilliseconds(100)))
                {
                    try
                    {
                        secondCompletion =
                            execution.CompletePlanAsync(
                                second,
                                DisposeAsync);
                        completedSecondWhileCoordinatorBlocked = true;
                    }
                    finally
                    {
                        Monitor.Exit(groupGate);
                    }
                }
            }
            finally
            {
                Monitor.Exit(coordinatorGate);
            }

            if (!completedSecondWhileCoordinatorBlocked)
            {
                secondCompletion = execution.CompletePlanAsync(
                    second,
                    DisposeAsync);
            }

            Task completedSecond = secondCompletion ??
                throw new InvalidOperationException();

            await Task.WhenAll(firstCompletion, completedSecond);
        }

        private SigningOperationPlan CreatePlan(string fileName)
        {
            FileInfo file = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    fileName));
            File.WriteAllText(file.FullName, file.Name);

            return new SigningOperationPlan(
                SigningFile.Capture(file),
                file);
        }

        private static Task DisposeAsync(
            SigningOperationCoordinator coordinator)
        {
            return coordinator.DisposeAsync().AsTask();
        }

        private static FieldInfo GetField(Type type, string name)
        {
            return type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic)!;
        }
    }
}
