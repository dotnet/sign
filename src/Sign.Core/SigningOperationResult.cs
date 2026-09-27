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

        internal FileInfo Materialize(FileInfo destination)
        {
            ArgumentNullException.ThrowIfNull(
                destination,
                nameof(destination));
            _coordinator.ThrowIfDisposed();

            IReadOnlyList<Materialization> materializations =
                CreateMaterializations(destination);

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
            FileInfo destination)
        {
            string destinationRoot = destination.DirectoryName!;
            string containedRoot = EnsureTrailingSeparator(
                Path.GetFullPath(destinationRoot));
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

            if (!destinationPath.StartsWith(
                destinationRoot,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    message:
                        "The signing operation result escapes its " +
                        "materialization root.");
            }

            return new FileInfo(destinationPath);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return Path.EndsInDirectorySeparator(path)
                ? path
                : $"{path}{Path.DirectorySeparatorChar}";
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
