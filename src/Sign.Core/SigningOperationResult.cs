// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SigningOperationResult
    {
        private readonly SigningOperationCoordinator _coordinator;
        private readonly IReadOnlyList<SnapshotFile> _snapshots;

        internal SigningOperationResult(
            IReadOnlyList<SnapshotFile> snapshots,
            SigningOperationCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(snapshots, nameof(snapshots));
            ArgumentNullException.ThrowIfNull(
                coordinator,
                nameof(coordinator));

            if (snapshots.Count == 0 ||
                snapshots.Count(snapshot => snapshot.IsPrimary) != 1)
            {
                throw new ArgumentException(
                    message:
                        "A signing operation result must contain exactly " +
                        "one primary snapshot.",
                    paramName: nameof(snapshots));
            }

            _snapshots = snapshots.ToArray();
            _coordinator = coordinator;
        }

        internal FileInfo Materialize(
            FileInfo destination,
            Func<FileInfo, FileInfo>? bindLayoutDestination = null)
        {
            ArgumentNullException.ThrowIfNull(
                destination,
                nameof(destination));
            _coordinator.ThrowIfDisposed();

            return Materialize(destination, CreateMaterializations(destination, bindLayoutDestination));
        }

        internal FileInfo MaterializePrimary(FileInfo destination)
        {
            ArgumentNullException.ThrowIfNull(destination, nameof(destination));
            _coordinator.ThrowIfDisposed();

            return Materialize(
                destination,
                new[] { new Materialization(_snapshots.Single(snapshot => snapshot.IsPrimary), destination) });
        }

        private static FileInfo Materialize(
            FileInfo destination,
            IReadOnlyList<Materialization> materializations)
        {
            foreach (Materialization materialization in materializations)
            {
                materialization.Destination.Directory!.Create();
                FileInfo temporaryDestination = new(
                    Path.Combine(
                        materialization.Destination.DirectoryName!,
                        $".sign-{Guid.NewGuid():N}.tmp"));

                try
                {
                    File.Copy(
                        sourceFileName:
                            materialization.Snapshot.File.FullName,
                        destFileName: temporaryDestination.FullName);

                    // The sibling temporary file allows atomic replacement.
                    File.Move(
                        sourceFileName: temporaryDestination.FullName,
                        destFileName:
                            materialization.Destination.FullName,
                        overwrite: true);
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException)
                {
                    Exception? cleanupException =
                        SigningOperationFile.TryDelete(
                            temporaryDestination);

                    if (cleanupException is not null)
                    {
                        throw new AggregateException(
                            exception,
                            cleanupException);
                    }

                    throw;
                }
            }

            return new FileInfo(destination.FullName);
        }

        private IReadOnlyList<Materialization> CreateMaterializations(
            FileInfo destination,
            Func<FileInfo, FileInfo>? bindLayoutDestination)
        {
            string destinationRoot = destination.DirectoryName!;
            string containedRoot = PathContainment.GetFullDirectoryPath(
                destinationRoot);
            List<Materialization> materializations =
                new(_snapshots.Count);
            HashSet<string> destinations =
                new(StringComparer.OrdinalIgnoreCase);

            foreach (
                SnapshotFile snapshot
                in _snapshots.OrderBy(
                    snapshot => snapshot.IsPrimary))
            {
                FileInfo materializationDestination = snapshot.IsPrimary
                    ? destination
                    : GetContainedDestination(
                        containedRoot,
                        snapshot.RelativePath);

                if (!snapshot.IsPrimary && bindLayoutDestination is not null)
                {
                    FileInfo bound = bindLayoutDestination(materializationDestination) ??
                        throw new InvalidOperationException("A layout destination binding returned no file.");
                    if (!string.Equals(bound.DirectoryName, materializationDestination.DirectoryName, StringComparison.Ordinal) ||
                        !string.Equals(bound.Name, materializationDestination.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "A layout destination binding may change only the spelling of its validated filename.");
                    }
                    materializationDestination = bound;
                }

                if (!destinations.Add(
                    materializationDestination.FullName))
                {
                    throw new InvalidOperationException(
                        message:
                            "The signing operation result contains " +
                            "conflicting materialization paths.");
                }

                materializations.Add(
                    new Materialization(
                        snapshot,
                        materializationDestination));
            }

            return materializations;
        }

        private static FileInfo GetContainedDestination(
            string destinationRoot,
            string relativePath)
        {
            if (Path.IsPathRooted(relativePath))
            {
                throw new InvalidOperationException(
                    message:
                        "The signing operation result contains a rooted " +
                        "relative path.");
            }

            string destinationPath = Path.GetFullPath(
                Path.Combine(destinationRoot, relativePath));

            if (!PathContainment.IsContained(
                destinationRoot,
                destinationPath))
            {
                throw new InvalidOperationException(
                    message:
                        "The signing operation result escapes its " +
                        "materialization root.");
            }

            return new FileInfo(destinationPath);
        }

        internal sealed class SnapshotFile
        {
            internal SnapshotFile(
                FileInfo file,
                string relativePath,
                bool isPrimary)
            {
                File = file;
                RelativePath = relativePath;
                IsPrimary = isPrimary;
            }

            internal FileInfo File { get; }
            internal string RelativePath { get; }
            internal bool IsPrimary { get; }
        }

        private sealed class Materialization
        {
            internal Materialization(
                SnapshotFile snapshot,
                FileInfo destination)
            {
                Snapshot = snapshot;
                Destination = destination;
            }

            internal SnapshotFile Snapshot { get; }
            internal FileInfo Destination { get; }
        }
    }

}
