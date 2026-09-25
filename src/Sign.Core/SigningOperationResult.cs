// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SigningOperationResult
    {
        private readonly SigningOperationCoordinator _coordinator;
        private readonly FileInfo _snapshot;

        internal SigningOperationResult(
            FileInfo snapshot,
            SigningOperationCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(snapshot, nameof(snapshot));
            ArgumentNullException.ThrowIfNull(
                coordinator,
                nameof(coordinator));

            _snapshot = snapshot;
            _coordinator = coordinator;
        }

        internal FileInfo Materialize(FileInfo destination)
        {
            ArgumentNullException.ThrowIfNull(
                destination,
                nameof(destination));
            _coordinator.ThrowIfDisposed();

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
}
