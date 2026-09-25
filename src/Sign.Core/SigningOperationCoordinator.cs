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
        private readonly object _gate = new();
        private readonly TemporaryDirectory _snapshotDirectory;
        private Task? _disposal;

        internal SigningOperationCoordinator(
            IDirectoryService directoryService)
        {
            ArgumentNullException.ThrowIfNull(
                directoryService,
                nameof(directoryService));

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
            ArgumentNullException.ThrowIfNull(identity, nameof(identity));
            ArgumentNullException.ThrowIfNull(operation, nameof(operation));

            OperationState candidate = new();
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
                }
            }

            if (ReferenceEquals(state, candidate))
            {
                state.Start(
                    operation,
                    CreateSnapshot);
            }

            return state.GetResultAsync();
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

        private SigningOperationResult CreateSnapshot(FileInfo artifact)
        {
            if (artifact is null)
            {
                throw new InvalidOperationException(
                    message: "The signing operation returned no artifact.");
            }

            FileInfo snapshot = new(
                Path.Combine(
                    _snapshotDirectory.Directory.FullName,
                    $"{Guid.NewGuid():N}"));

            try
            {
                File.Copy(
                    sourceFileName: artifact.FullName,
                    destFileName: snapshot.FullName);

                return new SigningOperationResult(
                    snapshot: snapshot,
                    coordinator: this);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                bool artifactIsUnavailable =
                    exception is FileNotFoundException ||
                    (exception is DirectoryNotFoundException &&
                        !File.Exists(artifact.FullName));
                Exception primaryException =
                    artifactIsUnavailable
                        ? new InvalidOperationException(
                            message: "The signed artifact is unavailable.",
                            innerException: exception)
                        : exception;
                Exception? cleanupException =
                    SigningOperationFile.TryDelete(snapshot);

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

        private sealed class OperationState
        {
            private static readonly AsyncLocal<OperationState?>
                s_executingState = new();
            private readonly
                TaskCompletionSource<SigningOperationResult> _completion =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal Task<SigningOperationResult> Completion =>
                _completion.Task;

            internal Task<SigningOperationResult> GetResultAsync()
            {
                if (Completion.IsCompleted)
                {
                    return Completion;
                }

                // This marker flows into child tasks. Until completion, any
                // same-identity claim from the owner's context is rejected as
                // self-await; delegates must not leave such work unawaited.
                // Planning must prevent cycles between distinct operations.
                if (ReferenceEquals(s_executingState.Value, this))
                {
                    throw new InvalidOperationException(
                        message: "A signing operation cannot await itself.");
                }

                return Completion;
            }

            internal void Start(
                Func<Task<FileInfo>> operation,
                Func<FileInfo, SigningOperationResult> snapshot)
            {
                _ = RunAsync(
                    operation,
                    snapshot);
            }

            private async Task RunAsync(
                Func<Task<FileInfo>> operation,
                Func<FileInfo, SigningOperationResult> snapshot)
            {
                Task<FileInfo> operationTask;
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
                    FileInfo artifact =
                        await operationTask.ConfigureAwait(
                            continueOnCapturedContext: false);
                    SigningOperationResult result = snapshot(artifact);
                    _completion.TrySetResult(result);
                }
                catch (Exception exception)
                {
                    PublishException(exception);
                }
            }

            private void PublishException(Exception exception)
            {
                // Fault rather than cancel the task so all waiters observe the
                // same OperationCanceledException instance.
                _completion.TrySetException(exception);
            }
        }
    }
}
