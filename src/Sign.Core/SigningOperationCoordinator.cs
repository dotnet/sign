// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SigningOperationCoordinator : IAsyncDisposable
    {
        private readonly
            Dictionary<SigningSourceIdentity, OperationState>
            _operations = new();
        private readonly
            Dictionary<Task<SigningOperationResult>, OperationState>
            _operationsByCompletion = new();
        private readonly object _gate = new();
        private readonly SigningOperationScope? _scope;
        private readonly TemporaryDirectory _snapshotDirectory;
        private readonly AsyncLocal<OwnerScheduler?> _scheduler = new();
        private readonly List<Task> _externalWork = new();
        private Task? _disposal;

        // A scoped coordinator serves one connected group of plans and
        // rejects requests outside the group's combined scope.
        internal SigningOperationCoordinator(
            IDirectoryService directoryService,
            SigningOperationScope? scope = null)
        {
            ArgumentNullException.ThrowIfNull(
                directoryService,
                nameof(directoryService));

            _scope = scope;
            _snapshotDirectory = new TemporaryDirectory(directoryService);
        }

        internal bool IsSchedulingEnabled => _scheduler.Value is not null;

        // Scheduling is invocation-local and flows through queued delegates.
        // Identity ownership (including out bool ownsOperation) is established
        // synchronously. A scheduled delegate starts only after acquiring its
        // own permit, which may be after ExecuteArtifactAsync returns.
        internal IDisposable UseScheduling(int maxConcurrency)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);

            lock (_gate)
            {
                ThrowIfDisposed();

                if (_scheduler.Value is not null)
                {
                    throw new InvalidOperationException(
                        "Scheduling scopes cannot overlap on one coordinator.");
                }

                _scheduler.Value = new OwnerScheduler(maxConcurrency);
            }

            return new SchedulingScope(_scheduler);
        }

        // Use only outside operation delegates, for preflight or publication.
        // Do not start or await owners while holding this lease. Disposal
        // also waits for these leases.
        internal Task<IDisposable> AcquireSchedulingLeaseAsync()
        {
            OperationState? executingState = OperationState.ExecutingState;

            if (executingState is not null && !executingState.Completion.IsCompleted)
            {
                throw new InvalidOperationException(
                    "External scheduling leases cannot be acquired inside an operation.");
            }

            TaskCompletionSource completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            OwnerScheduler? scheduler = _scheduler.Value;

            lock (_gate)
            {
                ThrowIfDisposed();
                _externalWork.Add(completion.Task);
            }

            return AcquireAsync();

            async Task<IDisposable> AcquireAsync()
            {
                try
                {
                    IDisposable? permit = scheduler is null
                        ? null
                        : await scheduler.AcquireAsync().ConfigureAwait(false);
                    return new ExternalWorkLease(permit, completion);
                }
                catch
                {
                    completion.TrySetResult();
                    throw;
                }
            }
        }

        // Results borrow coordinator-owned snapshots. ExecuteAsync is safe to
        // call concurrently with itself and DisposeAsync; new work is rejected
        // after disposal starts. Disposal awaits every published operation
        // before deleting snapshots; concurrent and repeated DisposeAsync
        // calls share that disposal and its outcome. Signing operations are
        // not cancellable, so disposal waits for the slowest in-flight
        // operation. Callers must finish their own materializations before
        // disposal, and operation delegates must not dispose the coordinator.
        internal Task<SigningOperationResult> ExecuteAsync(
            SigningSourceIdentity identity,
            Func<Task<FileInfo>> operation)
        {
            ArgumentNullException.ThrowIfNull(operation, nameof(operation));

            return ExecuteArtifactAsync(
                identity,
                ExecuteOperationAsync);

            async Task<SigningOperationArtifact> ExecuteOperationAsync()
            {
                Task<FileInfo> operationTask = operation();

                if (operationTask is null)
                {
                    throw new InvalidOperationException(
                        message:
                            "The signing operation returned no task.");
                }

                FileInfo artifact =
                    await operationTask.ConfigureAwait(
                        continueOnCapturedContext: false);

                if (artifact is null)
                {
                    throw new InvalidOperationException(
                        message:
                            "The signing operation returned no artifact.");
                }

                return SigningOperationArtifact.Single(artifact);
            }
        }

        // Unscheduled delegates start inline. Scheduled claims still report
        // ownership synchronously, even when their delegates queue.
        internal Task<SigningOperationResult> ExecuteArtifactAsync(
            SigningSourceIdentity identity,
            Func<Task<SigningOperationArtifact>> operation)
        {
            return ExecuteArtifactAsync(
                identity,
                operation,
                out _);
        }

        // Reports ownership synchronously, independently of scheduling.
        internal Task<SigningOperationResult> ExecuteArtifactAsync(
            SigningSourceIdentity identity,
            Func<Task<SigningOperationArtifact>> operation,
            out bool ownsOperation)
        {
            ArgumentNullException.ThrowIfNull(identity, nameof(identity));
            ArgumentNullException.ThrowIfNull(operation, nameof(operation));

            // A request outside the scope means the plan was wrongly found
            // isolated: another plan might need the same result.
            if (_scope is not null && !_scope.Contains(identity))
            {
                throw new InvalidOperationException(
                    message:
                        "A signing operation requested a file outside " +
                        "its scope.");
            }

            OperationState candidate = new(this, _scheduler.Value);
            OperationState state;

            lock (_gate)
            {
                ThrowIfDisposed();
                // Equal identities share one operation and its retained
                // snapshot.
                if (_operations.TryGetValue(
                    identity,
                    out OperationState? existing))
                {
                    state = existing;
                }
                else
                {
                    state = candidate;
                    _operations.Add(identity, state);
                    _operationsByCompletion.Add(state.Completion, state);
                }
            }

            ownsOperation = ReferenceEquals(state, candidate);

            if (ownsOperation)
            {
                state.Start(
                    operation,
                    CreateSnapshot);
            }

            return state.Completion;
        }

        // Returns only work that another caller already registered. This does
        // not claim an identity or start an operation.
        internal bool TryGetResult(
            SigningSourceIdentity identity,
            out Task<SigningOperationResult>? resultTask)
        {
            ArgumentNullException.ThrowIfNull(identity, nameof(identity));

            lock (_gate)
            {
                ThrowIfDisposed();

                if (_operations.TryGetValue(
                    identity,
                    out OperationState? state))
                {
                    resultTask = state.Completion;

                    return true;
                }

                resultTask = null;

                return false;
            }
        }

        // Operation delegates must use this method when awaiting another
        // coordinated result. Claims alone do not create dependencies.
        internal Task<SigningOperationResult> WaitAsync(
            Task<SigningOperationResult> resultTask)
        {
            ArgumentNullException.ThrowIfNull(
                resultTask,
                nameof(resultTask));

            WaitRegistration? registration = RegisterWait(resultTask);

            return registration is null
                ? resultTask
                : AwaitRegisteredAsync(
                    resultTask,
                    registration);
        }

        // The alternative task is a progress milestone owned by the same
        // operation as resultTask. The dependency lasts until either task
        // completes.
        internal Task<Task> WaitAsync(
            Task<SigningOperationResult> resultTask,
            Task alternativeTask)
        {
            ArgumentNullException.ThrowIfNull(
                resultTask,
                nameof(resultTask));
            ArgumentNullException.ThrowIfNull(
                alternativeTask,
                nameof(alternativeTask));

            WaitRegistration? registration =
                RegisterWait(resultTask, alternativeTask);

            return registration is null
                ? Task.WhenAny(resultTask, alternativeTask)
                : AwaitRegisteredAsync(
                    Task.WhenAny(resultTask, alternativeTask),
                    registration);
        }

        // These APIs express one actual suspension boundary. Await the
        // returned task before doing further owner work. Do not overlap them
        // within an owner: use the all form for concurrent prerequisite waits.
        internal Task<SigningOperationResult> WaitForReadyOwnerAsync(
            Task<SigningOperationResult> resultTask)
        {
            ArgumentNullException.ThrowIfNull(resultTask);

            if (OperationState.ExecutingState?.Scheduler is null)
            {
                return WaitAsync(resultTask);
            }

            ReadyWaitScope scope = BeginReadyWait(new[] { resultTask });
            return AwaitReadyAsync(resultTask, scope);
        }

        internal Task<Task> WaitForReadyOwnerAsync(
            Task<SigningOperationResult> resultTask,
            Task alternativeTask)
        {
            ArgumentNullException.ThrowIfNull(resultTask);
            ArgumentNullException.ThrowIfNull(alternativeTask);

            if (OperationState.ExecutingState?.Scheduler is null)
            {
                return WaitAsync(resultTask, alternativeTask);
            }

            ReadyWaitScope scope = BeginReadyWait(
                new[] { resultTask }, alternativeTask);
            return AwaitReadyAsync(
                Task.WhenAny(resultTask, alternativeTask), scope);
        }

        internal Task<SigningOperationResult[]> WaitAllForReadyOwnerAsync(
            IEnumerable<Task<SigningOperationResult>> results)
        {
            Task<SigningOperationResult>[] tasks = GetWaitTasks(results);
            ReadyWaitScope scope = BeginReadyWait(tasks);
            return AwaitReadyAsync(Task.WhenAll(tasks), scope);
        }

        private static Task<SigningOperationResult>[] GetWaitTasks(
            IEnumerable<Task<SigningOperationResult>> results)
        {
            ArgumentNullException.ThrowIfNull(results);
            Task<SigningOperationResult>[] tasks = results.ToArray();

            if (tasks.Any(task => task is null))
            {
                throw new ArgumentException(
                    "Result tasks cannot be null.", nameof(results));
            }

            return tasks;
        }

        private ReadyWaitScope BeginReadyWait(
            Task<SigningOperationResult>[] tasks,
            Task? alternativeTask = null)
        {
            OperationState? state = OperationState.ExecutingState;
            ReadyWaitScope scope = new(state);

            lock (_gate)
            {
                // A captured ExecutionContext can outlive its operation.
                // Such callers no longer have an owner permit to suspend.
                bool scheduled = state?.Scheduler is not null &&
                    (state.Permit is not null || state.ReadyWait is not null);

                if (scheduled && state!.ReadyWait is not null)
                {
                    throw new InvalidOperationException(
                        "Ready-owner wait scopes cannot overlap. " +
                        "Use an aggregate-aware wait.");
                }

                try
                {
                    foreach (Task<SigningOperationResult> task in tasks)
                    {
                        WaitRegistration? registration =
                            RegisterWait(task, alternativeTask);

                        if (registration is not null)
                        {
                            scope.Registrations.Add(registration);
                        }
                    }
                }
                catch
                {
                    foreach (WaitRegistration registration in scope.Registrations)
                    {
                        registration.Waiting.RemoveWait(registration);
                    }

                    throw;
                }

                if (scheduled && scope.Registrations.Count > 0)
                {
                    state!.ReadyWait = scope;
                    scope.IsSuspended = true;
                    state.ReleasePermit();
                }
            }

            return scope;
        }

        private async Task<T> AwaitReadyAsync<T>(
            Task<T> task,
            ReadyWaitScope scope)
        {
            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                // Drop edges at the actual progress boundary, before queuing
                // runnable work. A reacquiring owner is no longer dependent.
                lock (_gate)
                {
                    foreach (WaitRegistration registration in scope.Registrations)
                    {
                        registration.Waiting.RemoveWait(registration);
                    }
                }

                if (scope.IsSuspended)
                {
                    OperationState state = scope.State!;
                    IDisposable permit = await state.Scheduler!.AcquireAsync()
                        .ConfigureAwait(false);

                    lock (_gate)
                    {
                        state.Permit = permit;
                        state.ReadyWait = null;
                    }
                }

                scope.Completion.TrySetResult();
            }
        }

        private WaitRegistration? RegisterWait(
                Task<SigningOperationResult> resultTask,
                Task? alternativeTask = null)
        {
            OperationState? waitingState =
                OperationState.ExecutingState;

            if (waitingState is null)
            {
                return null;
            }

            if (!ReferenceEquals(waitingState.Coordinator, this))
            {
                throw new InvalidOperationException(
                    message:
                        "A signing operation cannot await work from " +
                        "another coordinator.");
            }

            lock (_gate)
            {
                if (!_operationsByCompletion.TryGetValue(
                    resultTask,
                    out OperationState? awaitedState))
                {
                    throw new ArgumentException(
                        message:
                            "The result task does not belong to this " +
                            "coordinator.",
                        paramName: nameof(resultTask));
                }

                if (resultTask.IsCompleted ||
                    alternativeTask?.IsCompleted == true)
                {
                    return null;
                }

                if (ReferenceEquals(waitingState, awaitedState))
                {
                    throw new InvalidOperationException(
                        message:
                            "A signing operation cannot await itself.");
                }

                WaitRegistration registration = new(
                    waitingState,
                    awaitedState,
                    resultTask,
                    alternativeTask);
                waitingState.AddWait(registration);

                if (awaitedState.Reaches(waitingState))
                {
                    waitingState.RemoveWait(registration);

                    throw new InvalidOperationException(
                        message:
                            "A signing operation dependency cycle was " +
                            "detected.");
                }

                return registration;
            }
        }

        private async Task<T> AwaitRegisteredAsync<T>(
            Task<T> task,
            WaitRegistration registration)
        {
            try
            {
                return await task.ConfigureAwait(
                    continueOnCapturedContext: false);
            }
            finally
            {
                lock (_gate)
                {
                    registration.Waiting.RemoveWait(registration);
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            Task disposal;

            lock (_gate)
            {
                disposal = _disposal ??= DisposeCoreAsync(
                    _operations.Values.ToArray(),
                    _externalWork.ToArray());
            }

            return new ValueTask(disposal);
        }

        private async Task DisposeCoreAsync(
            OperationState[] states,
            Task[] externalWork)
        {
            // Leave _gate before waiting or deleting snapshots.
            await Task.Yield();

            foreach (OperationState state in states)
            {
                try
                {
                    await state.Completion.ConfigureAwait(
                        continueOnCapturedContext: false);
                }
                catch
                {
                    // Operation faults are observed by their waiters.
                }
            }

            await Task.WhenAll(externalWork).ConfigureAwait(false);
            _snapshotDirectory.Dispose();
        }

        internal void ThrowIfDisposed()
        {
            if (_disposal is not null)
            {
                throw new ObjectDisposedException(
                    objectName: nameof(SigningOperationCoordinator));
            }
        }

        private SigningOperationResult CreateSnapshot(
            SigningOperationArtifact artifact)
        {
            if (artifact is null)
            {
                throw new InvalidOperationException(
                    message: "The signing operation returned no artifact.");
            }

            DirectoryInfo snapshotDirectory = new(
                Path.Combine(
                    _snapshotDirectory.Directory.FullName,
                    $"{Guid.NewGuid():N}"));
            List<SigningOperationResult.SnapshotFile> snapshots = new();

            try
            {
                _snapshotDirectory.Directory.Refresh();

                if (!_snapshotDirectory.Directory.Exists)
                {
                    throw new DirectoryNotFoundException(
                        _snapshotDirectory.Directory.FullName);
                }

                artifact.PrimaryFile.Refresh();

                if (!artifact.PrimaryFile.Exists)
                {
                    throw new InvalidOperationException(
                        message: Resources.SignedArtifactUnavailable,
                        innerException: new FileNotFoundException(
                            message:
                                "The primary signing artifact does " +
                                "not exist.",
                            fileName:
                                artifact.PrimaryFile.FullName));
                }

                string rootPath = PathContainment.GetFullDirectoryPath(
                    artifact.RootDirectory.FullName);
                IReadOnlyList<FileInfo> files = artifact.GetFiles();

                foreach (FileInfo file in files)
                {
                    string sourcePath = Path.GetFullPath(file.FullName);

                    if (!PathContainment.IsContained(rootPath, sourcePath))
                    {
                        throw new InvalidOperationException(
                            message:
                                "A signing operation artifact escapes " +
                                "its root directory.");
                    }

                    string relativePath = Path.GetRelativePath(
                        artifact.RootDirectory.FullName,
                        sourcePath);
                    FileInfo snapshot = artifact.IncludeSiblings
                        ? new FileInfo(
                            Path.Combine(
                                snapshotDirectory.FullName,
                                relativePath))
                        : new FileInfo(
                            snapshotDirectory.FullName);

                    if (artifact.IncludeSiblings)
                    {
                        snapshot.Directory!.Create();
                    }
                    File.Copy(
                        sourceFileName: sourcePath,
                        destFileName: snapshot.FullName);
                    snapshots.Add(
                        new SigningOperationResult.SnapshotFile(
                            snapshot,
                            relativePath,
                            isPrimary: string.Equals(
                                sourcePath,
                                Path.GetFullPath(
                                    artifact.PrimaryFile.FullName),
                                StringComparison.OrdinalIgnoreCase)));
                }

                return new SigningOperationResult(
                    snapshots,
                    coordinator: this);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                bool artifactIsUnavailable =
                    exception is FileNotFoundException ||
                    (exception is DirectoryNotFoundException &&
                        !File.Exists(
                            artifact.PrimaryFile.FullName));
                Exception primaryException =
                    artifactIsUnavailable
                        ? new InvalidOperationException(
                            message:
                                Resources.SignedArtifactUnavailable,
                            innerException: exception)
                        : exception;
                Exception? cleanupException =
                    artifact.IncludeSiblings
                        ? SigningOperationDirectory.TryDelete(
                            snapshotDirectory)
                        : SigningOperationFile.TryDelete(
                            new FileInfo(
                                snapshotDirectory.FullName));

                if (cleanupException is not null)
                {
                    throw new AggregateException(
                        primaryException,
                        cleanupException);
                }

                if (!ReferenceEquals(primaryException, exception))
                {
                    throw primaryException;
                }

                throw;
            }
        }

        private sealed class OwnerScheduler
        {
            private readonly SemaphoreSlim _permits;

            internal OwnerScheduler(int maxConcurrency)
            {
                _permits = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            }

            internal async Task<IDisposable> AcquireAsync()
            {
                await _permits.WaitAsync().ConfigureAwait(false);
                return new Permit(_permits);
            }

            private sealed class Permit : IDisposable
            {
                private SemaphoreSlim? _permits;

                internal Permit(SemaphoreSlim permits) => _permits = permits;

                public void Dispose() =>
                    Interlocked.Exchange(ref _permits, null)?.Release();
            }
        }

        private sealed class SchedulingScope : IDisposable
        {
            private AsyncLocal<OwnerScheduler?>? _scheduler;

            internal SchedulingScope(AsyncLocal<OwnerScheduler?> scheduler) =>
                _scheduler = scheduler;

            public void Dispose()
            {
                AsyncLocal<OwnerScheduler?>? scheduler =
                    Interlocked.Exchange(ref _scheduler, null);

                if (scheduler is not null)
                {
                    scheduler.Value = null;
                }
            }
        }

        private sealed class ExternalWorkLease : IDisposable
        {
            private readonly IDisposable? _permit;
            private TaskCompletionSource? _completion;

            internal ExternalWorkLease(
                IDisposable? permit,
                TaskCompletionSource completion)
            {
                _permit = permit;
                _completion = completion;
            }

            public void Dispose()
            {
                TaskCompletionSource? completion =
                    Interlocked.Exchange(ref _completion, null);

                if (completion is not null)
                {
                    try
                    {
                        _permit?.Dispose();
                    }
                    finally
                    {
                        completion.TrySetResult();
                    }
                }
            }
        }

        private sealed class ReadyWaitScope
        {
            internal ReadyWaitScope(OperationState? state) => State = state;

            internal OperationState? State { get; }
            internal List<WaitRegistration> Registrations { get; } = new();
            internal TaskCompletionSource Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal bool IsSuspended { get; set; }
        }

        private sealed class WaitRegistration
        {
            private readonly Task _resultTask;
            private readonly Task? _alternativeTask;

            internal WaitRegistration(
                OperationState waiting,
                OperationState awaited,
                Task resultTask,
                Task? alternativeTask)
            {
                Waiting = waiting;
                Awaited = awaited;
                _resultTask = resultTask;
                _alternativeTask = alternativeTask;
            }

            internal OperationState Waiting { get; }

            internal OperationState Awaited { get; }

            internal bool IsCompleted =>
                _resultTask.IsCompleted ||
                _alternativeTask?.IsCompleted == true;
        }

        private sealed class OperationState
        {
            private static readonly AsyncLocal<OperationState?>
                s_executingState = new();
            private readonly
                TaskCompletionSource<SigningOperationResult> _completion =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly List<WaitRegistration> _waits = new();

            internal OperationState(
                SigningOperationCoordinator coordinator,
                OwnerScheduler? scheduler)
            {
                Coordinator = coordinator;
                Scheduler = scheduler;
            }

            internal OwnerScheduler? Scheduler { get; }
            internal IDisposable? Permit { get; set; }
            internal ReadyWaitScope? ReadyWait { get; set; }

            internal Task<SigningOperationResult> Completion =>
                _completion.Task;

            internal SigningOperationCoordinator Coordinator { get; }

            internal static OperationState? ExecutingState =>
                s_executingState.Value;

            internal void AddWait(WaitRegistration registration)
            {
                _waits.Add(registration);
            }

            internal void RemoveWait(WaitRegistration registration)
            {
                _waits.Remove(registration);
            }

            internal bool Reaches(OperationState target)
            {
                HashSet<OperationState> visited = new();
                Stack<OperationState> pending = new();
                pending.Push(this);

                while (pending.Count > 0)
                {
                    OperationState state = pending.Pop();

                    if (!visited.Add(state))
                    {
                        continue;
                    }

                    if (ReferenceEquals(state, target))
                    {
                        return true;
                    }

                    for (int i = state._waits.Count - 1; i >= 0; i--)
                    {
                        WaitRegistration registration = state._waits[i];

                        if (registration.IsCompleted)
                        {
                            state._waits.RemoveAt(i);
                        }
                        else
                        {
                            pending.Push(registration.Awaited);
                        }
                    }
                }

                return false;
            }

            internal void Start(
                Func<Task<SigningOperationArtifact>> operation,
                Func<SigningOperationArtifact, SigningOperationResult>
                    snapshot)
            {
                _ = Scheduler is null
                    ? RunAsync(operation, snapshot)
                    : RunScheduledAsync(operation, snapshot);
            }

            internal void ReleasePermit()
            {
                Permit?.Dispose();
                Permit = null;
            }

            private async Task EnsureReadyAsync()
            {
                Task? readiness;

                lock (Coordinator._gate)
                {
                    readiness = ReadyWait?.Completion.Task;
                }

                if (readiness is not null)
                {
                    await readiness.ConfigureAwait(false);
                }
            }

            private async Task RunScheduledAsync(
                Func<Task<SigningOperationArtifact>> operation,
                Func<SigningOperationArtifact, SigningOperationResult> snapshot)
            {
                Permit = await Scheduler!.AcquireAsync().ConfigureAwait(false);
                OperationState? previousState = s_executingState.Value;
                s_executingState.Value = this;
                SigningOperationArtifact? artifact = null;
                SigningOperationResult? result = null;
                Exception? failure = null;

                try
                {
                    Task<SigningOperationArtifact> operationTask = operation()
                        ?? throw new InvalidOperationException(
                            "The signing operation returned no task.");
                    artifact = await operationTask.ConfigureAwait(false);
                    await EnsureReadyAsync().ConfigureAwait(false);
                    result = snapshot(artifact);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    // Even an unawaited aware wait must restore ownership
                    // before coordinator cleanup can run.
                    await EnsureReadyAsync().ConfigureAwait(false);

                    try
                    {
                        artifact?.Dispose();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                    finally
                    {
                        lock (Coordinator._gate)
                        {
                            ReleasePermit();
                        }

                        s_executingState.Value = previousState;
                    }
                }

                if (failure is not null)
                {
                    PublishException(failure);
                }
                else
                {
                    _completion.TrySetResult(result!);
                }
            }

            private async Task RunAsync(
                Func<Task<SigningOperationArtifact>> operation,
                Func<SigningOperationArtifact, SigningOperationResult>
                    snapshot)
            {
                Task<SigningOperationArtifact> operationTask;
                OperationState? previousState = s_executingState.Value;
                s_executingState.Value = this;

                try
                {
                    // The delegate must fully write and flush the artifact and
                    // close every writer before its task completes.
                    operationTask = operation();
                }
                catch (Exception exception)
                {
                    PublishException(exception);

                    return;
                }
                finally
                {
                    s_executingState.Value = previousState;
                }

                if (operationTask is null)
                {
                    PublishException(
                        new InvalidOperationException(
                            message:
                                "The signing operation returned no task."));

                    return;
                }

                try
                {
                    using SigningOperationArtifact artifact =
                        await operationTask.ConfigureAwait(
                            continueOnCapturedContext: false);
                    SigningOperationResult result =
                        snapshot(artifact);
                    _completion.TrySetResult(result);
                }
                catch (Exception exception)
                {
                    PublishException(exception);
                }
            }

            private void PublishException(Exception exception)
            {
                // Fault even for cancellation exceptions so every waiter
                // observes the same original exception instance.
                _completion.TrySetException(exception);
            }
        }

        internal static class SigningOperationDirectory
        {
            internal static Exception? TryDelete(DirectoryInfo directory)
            {
                try
                {
                    directory.Delete(recursive: true);

                    return null;
                }
                catch (Exception exception) when (
                    exception is DirectoryNotFoundException)
                {
                    return null;
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException)
                {
                    return exception;
                }
            }
        }
    }
}
