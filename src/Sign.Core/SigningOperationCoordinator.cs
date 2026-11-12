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
        private readonly Dictionary<
            SigningSourceIdentity,
            HashSet<SigningOperationScope>>?
            _consumerScopesByFile;
        private readonly Dictionary<
            string,
            HashSet<SigningOperationScope>>?
            _consumerScopesByDirectory;
        private readonly Dictionary<
            SigningOperationScope,
            HashSet<OperationState>>?
            _operationsByConsumerScope;
        private readonly object _gate = new();
        private readonly HashSet<SigningOperationScope>?
            _remainingScopes;
        private readonly SigningOperationScope? _scope;
        private readonly TemporaryDirectory _snapshotDirectory;
        private Task? _disposal;

        // A scoped coordinator serves one connected group of plans and
        // rejects requests outside the group's combined scope.
        internal SigningOperationCoordinator(
            IDirectoryService directoryService,
            SigningOperationScope? scope = null,
            IReadOnlyCollection<SigningOperationScope>?
                potentialConsumerScopes = null)
        {
            ArgumentNullException.ThrowIfNull(
                directoryService,
                nameof(directoryService));

            if (potentialConsumerScopes is not null && scope is null)
            {
                throw new ArgumentException(
                    message:
                        "Potential consumer scopes require a combined " +
                        "coordinator scope.",
                    paramName: nameof(potentialConsumerScopes));
            }

            _scope = scope;
            _remainingScopes = potentialConsumerScopes is null
                ? null
                : new HashSet<SigningOperationScope>(
                    potentialConsumerScopes);

            if (potentialConsumerScopes is not null)
            {
                _consumerScopesByFile = new();
                _consumerScopesByDirectory =
                    new(StringComparer.OrdinalIgnoreCase);
                _operationsByConsumerScope = new();

                foreach (
                    SigningOperationScope potentialConsumerScope
                    in potentialConsumerScopes)
                {
                    _operationsByConsumerScope.TryAdd(
                        potentialConsumerScope,
                        new HashSet<OperationState>());

                    foreach (
                        SigningSourceIdentity file
                        in potentialConsumerScope.Files)
                    {
                        AddConsumerScope(
                            _consumerScopesByFile,
                            file,
                            potentialConsumerScope);
                    }

                    foreach (
                        SigningSourceIdentity directoryTree
                        in potentialConsumerScope.DirectoryTrees)
                    {
                        string directory =
                            PathContainment.GetFullDirectoryPath(
                                Path.GetDirectoryName(
                                    directoryTree.GetPhysicalPath())!);
                        AddConsumerScope(
                            _consumerScopesByDirectory,
                            directory,
                            potentialConsumerScope);
                    }
                }
            }

            _snapshotDirectory = new TemporaryDirectory(directoryService);
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

        // A claiming caller's operation is invoked before this method
        // returns, so callers can learn synchronously which identities they
        // own.
        internal Task<SigningOperationResult> ExecuteArtifactAsync(
            SigningSourceIdentity identity,
            Func<Task<SigningOperationArtifact>> operation)
        {
            return ExecuteArtifactAsync(
                identity,
                operation,
                out _);
        }

        // Reports ownership synchronously because a claiming caller's
        // operation is invoked before this method returns.
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

            OperationState candidate = new(this, identity);
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

                    if (_remainingScopes is not null)
                    {
                        foreach (
                            SigningOperationScope consumerScope
                            in GetConsumerScopes(identity))
                        {
                            if (_remainingScopes.Contains(consumerScope))
                            {
                                state.AddPotentialConsumer();
                                _operationsByConsumerScope![consumerScope]
                                    .Add(state);
                            }
                        }
                    }
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

        // A scope can make no more requests after its plan completes.
        // Completed snapshots are released once no unfinished scope could
        // request their identity.
        internal void CompleteScope(SigningOperationScope scope)
        {
            CompleteScope(scope, out _);
        }

        internal void CompleteScope(
            SigningOperationScope scope,
            out int operationCount)
        {
            ArgumentNullException.ThrowIfNull(scope, nameof(scope));
            List<SigningOperationResult>? resultsToRelease = null;
            operationCount = 0;

            lock (_gate)
            {
                ThrowIfDisposed();

                if (_remainingScopes is null ||
                    !_remainingScopes.Remove(scope))
                {
                    throw new InvalidOperationException(
                        message:
                            "The signing operation scope was not active.");
                }

                HashSet<OperationState> operations =
                    _operationsByConsumerScope![scope];
                _operationsByConsumerScope.Remove(scope);
                operationCount = operations.Count;

                foreach (OperationState operation in operations)
                {
                    if (operation.CompletePotentialConsumer() != 0 ||
                        !operation.Completion.IsCompleted)
                    {
                        continue;
                    }

                    _operations.Remove(operation.Identity);
                    _operationsByCompletion.Remove(
                        operation.Completion);

                    if (!operation.Completion.IsCompletedSuccessfully)
                    {
                        continue;
                    }

                    resultsToRelease ??=
                        new List<SigningOperationResult>();
                    resultsToRelease.Add(operation.Completion.Result);
                }
            }

            List<Exception>? cleanupExceptions = null;

            foreach (
                SigningOperationResult result
                in resultsToRelease ?? Enumerable.Empty<
                    SigningOperationResult>())
            {
                try
                {
                    result.ReleaseSnapshot();
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException)
                {
                    cleanupExceptions ??= new List<Exception>();
                    cleanupExceptions.Add(exception);
                }
            }

            if (cleanupExceptions?.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(cleanupExceptions[0])
                    .Throw();
            }

            if (cleanupExceptions?.Count > 1)
            {
                throw new AggregateException(cleanupExceptions);
            }
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
                    _operations.Values.ToArray());
            }

            return new ValueTask(disposal);
        }

        private async Task DisposeCoreAsync(OperationState[] states)
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

        private IReadOnlySet<SigningOperationScope> GetConsumerScopes(
            SigningSourceIdentity identity)
        {
            HashSet<SigningOperationScope> scopes = new();
            SigningSourceIdentity physicalRoot = identity.GetPhysicalRoot();

            if (_consumerScopesByFile!.TryGetValue(
                physicalRoot,
                out HashSet<SigningOperationScope>? fileScopes))
            {
                scopes.UnionWith(fileScopes);
            }

            string directory = PathContainment.GetFullDirectoryPath(
                Path.GetDirectoryName(physicalRoot.GetPhysicalPath())!);

            while (true)
            {
                if (_consumerScopesByDirectory!.TryGetValue(
                    directory,
                    out HashSet<SigningOperationScope>? directoryScopes))
                {
                    scopes.UnionWith(directoryScopes);
                }

                string root = PathContainment.GetFullDirectoryPath(
                    Path.GetPathRoot(directory)!);

                if (string.Equals(
                    directory,
                    root,
                    StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                directory = PathContainment.GetFullDirectoryPath(
                    Path.GetDirectoryName(
                        Path.TrimEndingDirectorySeparator(directory))!);
            }

            return scopes;
        }

        private static void AddConsumerScope<TKey>(
            Dictionary<TKey, HashSet<SigningOperationScope>> scopesByKey,
            TKey key,
            SigningOperationScope scope)
            where TKey : notnull
        {
            if (!scopesByKey.TryGetValue(
                key,
                out HashSet<SigningOperationScope>? scopes))
            {
                scopes = new HashSet<SigningOperationScope>();
                scopesByKey.Add(key, scopes);
            }

            scopes.Add(scope);
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
                IReadOnlyList<FileInfo> files =
                    artifact.IncludeSiblings
                        ? artifact.RootDirectory
                            .EnumerateFiles(
                                "*",
                                SearchOption.AllDirectories)
                            .ToList()
                        : new[] { artifact.PrimaryFile };

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
                    artifact.IncludeSiblings
                        ? snapshotDirectory
                        : new FileInfo(snapshotDirectory.FullName),
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

            private int _potentialConsumerCount;

            internal OperationState(
                SigningOperationCoordinator coordinator,
                SigningSourceIdentity identity)
            {
                Coordinator = coordinator;
                Identity = identity;
            }

            internal Task<SigningOperationResult> Completion =>
                _completion.Task;

            internal SigningOperationCoordinator Coordinator { get; }

            internal SigningSourceIdentity Identity { get; }

            internal static OperationState? ExecutingState =>
                s_executingState.Value;

            internal void AddPotentialConsumer()
            {
                ++_potentialConsumerCount;
            }

            internal int CompletePotentialConsumer()
            {
                return --_potentialConsumerCount;
            }

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
                _ = RunAsync(
                    operation,
                    snapshot);
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
