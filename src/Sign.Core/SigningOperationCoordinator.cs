// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;

namespace Sign.Core
{
    internal sealed class SigningOperationCoordinator : IDisposable
    {
        private readonly
            ConcurrentDictionary<SigningSourceIdentity, OperationState>
            _operations = new();
        private readonly TemporaryDirectory _snapshotDirectory;
        private readonly SigningOperationSnapshotLifetime _snapshotLifetime;

        internal SigningOperationCoordinator(
            IDirectoryService directoryService)
        {
            ArgumentNullException.ThrowIfNull(
                directoryService,
                nameof(directoryService));

            _snapshotDirectory = new TemporaryDirectory(directoryService);
            _snapshotLifetime = new SigningOperationSnapshotLifetime(
                cleanup: _snapshotDirectory.Dispose);
        }

        // Results borrow coordinator-owned snapshots. Dispose only after all
        // operations and materializations finish. Disposal rejects new work
        // but does not cancel work already running.
        internal Task<SigningOperationResult> ExecuteAsync(
            SigningSourceIdentity identity,
            Func<Task<FileInfo>> operation)
        {
            ArgumentNullException.ThrowIfNull(identity, nameof(identity));
            ArgumentNullException.ThrowIfNull(operation, nameof(operation));
            _snapshotLifetime.ThrowIfDisposed();

            OperationState candidate = new(operation, CreateSnapshot);
            // Equal identities share one operation and its retained snapshot.
            OperationState state = _operations.GetOrAdd(identity, candidate);

            return state.GetResultAsync();
        }

        public void Dispose()
        {
            _snapshotLifetime.Dispose();
        }

        private SigningOperationResult CreateSnapshot(FileInfo artifact)
        {
            using IDisposable lease =
                _snapshotLifetime.EnterOperation();

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
                    snapshotLifetime: _snapshotLifetime);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                Exception primaryException =
                    exception is FileNotFoundException ||
                    exception is DirectoryNotFoundException &&
                        !File.Exists(artifact.FullName)
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
            private readonly Func<Task<FileInfo>> _operation;
            private readonly Func<FileInfo, SigningOperationResult> _snapshot;
            private readonly
                TaskCompletionSource<SigningOperationResult> _completion =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _started;

            internal OperationState(
                Func<Task<FileInfo>> operation,
                Func<FileInfo, SigningOperationResult> snapshot)
            {
                _operation = operation;
                _snapshot = snapshot;
            }

            internal Task<SigningOperationResult> GetResultAsync()
            {
                if (_completion.Task.IsCompleted)
                {
                    return _completion.Task;
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

                if (Interlocked.Exchange(ref _started, value: 1) == 0)
                {
                    Start();
                }

                return _completion.Task;
            }

            private void Start()
            {
                Task<FileInfo> operationTask;
                OperationState? previousState = s_executingState.Value;
                s_executingState.Value = this;

                try
                {
                    // The delegate must fully write and flush the artifact and
                    // close every writer before its task completes.
                    operationTask = _operation();
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

                _ = CompleteAsync(operationTask);
            }

            private async Task CompleteAsync(Task<FileInfo> operationTask)
            {
                try
                {
                    FileInfo artifact =
                        await operationTask.ConfigureAwait(
                            continueOnCapturedContext: false);
                    SigningOperationResult result = _snapshot(artifact);
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
