// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SigningOperationResult
    {
        private readonly FileInfo _snapshot;
        private readonly SigningOperationSnapshotLifetime _snapshotLifetime;

        internal SigningOperationResult(
            FileInfo snapshot,
            SigningOperationSnapshotLifetime snapshotLifetime)
        {
            ArgumentNullException.ThrowIfNull(snapshot, nameof(snapshot));
            ArgumentNullException.ThrowIfNull(
                snapshotLifetime,
                nameof(snapshotLifetime));

            _snapshot = snapshot;
            _snapshotLifetime = snapshotLifetime;
        }

        internal FileInfo Materialize(FileInfo destination)
        {
            ArgumentNullException.ThrowIfNull(
                destination,
                nameof(destination));
            using IDisposable lease =
                _snapshotLifetime.EnterOperation();

            destination.Directory?.Create();
            FileInfo temporaryDestination = new(
                Path.Combine(
                    destination.DirectoryName!,
                    $".sign-{Guid.NewGuid():N}.tmp"));

            try
            {
                File.Copy(
                    sourceFileName: _snapshot.FullName,
                    destFileName: temporaryDestination.FullName);

                // The sibling temporary file allows atomic replacement.
                File.Move(
                    sourceFileName: temporaryDestination.FullName,
                    destFileName: destination.FullName,
                    overwrite: true);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                Exception? cleanupException =
                    SigningOperationFile.TryDelete(temporaryDestination);

                if (cleanupException is not null)
                {
                    throw new AggregateException(
                        exception,
                        cleanupException);
                }

                throw;
            }

            return new FileInfo(destination.FullName);
        }
    }

    internal sealed class SigningOperationSnapshotLifetime : IDisposable
    {
        private readonly object _gate = new();
        private Action? _cleanup;
        private int _activeOperationCount;
        private int _disposed;

        internal SigningOperationSnapshotLifetime(Action cleanup)
        {
            ArgumentNullException.ThrowIfNull(cleanup, nameof(cleanup));

            _cleanup = cleanup;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, value: 1) != 0)
            {
                return;
            }

            Action? cleanup = null;

            lock (_gate)
            {
                if (_activeOperationCount == 0)
                {
                    cleanup = TakeCleanup();
                }
            }

            cleanup?.Invoke();
        }

        internal IDisposable EnterOperation()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                ++_activeOperationCount;

                return new OperationLease(lifetime: this);
            }
        }

        internal void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(
                    objectName: nameof(SigningOperationCoordinator));
            }
        }

        private void ExitOperation()
        {
            Action? cleanup = null;

            lock (_gate)
            {
                _activeOperationCount--;

                if (_activeOperationCount == 0 &&
                    Volatile.Read(ref _disposed) != 0)
                {
                    cleanup = TakeCleanup();
                }
            }

            cleanup?.Invoke();
        }

        private Action? TakeCleanup()
        {
            Action? cleanup = _cleanup;
            _cleanup = null;

            return cleanup;
        }

        private sealed class OperationLease : IDisposable
        {
            private SigningOperationSnapshotLifetime? _lifetime;

            internal OperationLease(
                SigningOperationSnapshotLifetime lifetime)
            {
                _lifetime = lifetime;
            }

            public void Dispose()
            {
                Interlocked.Exchange(
                    ref _lifetime,
                    value: null)?.ExitOperation();
            }
        }
    }

    internal static class SigningOperationFile
    {
        internal static Exception? TryDelete(FileInfo file)
        {
            try
            {
                file.Delete();

                return null;
            }
            catch (Exception exception) when (
                exception is FileNotFoundException or
                DirectoryNotFoundException)
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
