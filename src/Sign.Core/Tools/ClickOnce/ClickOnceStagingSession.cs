// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ClickOnceStagingSession : IDisposable
    {
        private readonly IReadOnlyList<ClickOnceManifestDiagnostic>
            _diagnostics;
        private readonly
            IReadOnlyDictionary<BaseReference, string?> _originalResolvedPaths;
        private readonly IReadOnlyList<MappedFile> _mappedFiles;
        private readonly TemporaryDirectory _temporaryDirectory;
        private bool _disposed;
        private bool _manifestFileInfoUpdateActive;

        internal ClickOnceStagingSession(
            IReadOnlyList<ClickOnceManifestDiagnostic> diagnostics,
            TemporaryDirectory temporaryDirectory,
            ClickOnceStagedFile? deploymentManifest,
            ClickOnceStagedFile applicationManifest,
            IReadOnlyDictionary<
                ResolvedClickOncePayload,
                ClickOnceStagedFile> payloads,
            IReadOnlyDictionary<
                ResolvedClickOnceAdjacentExecutable,
                ClickOnceStagedFile> adjacentExecutables,
            IReadOnlyDictionary<BaseReference, string?>
                originalResolvedPaths)
        {
            ArgumentNullException.ThrowIfNull(diagnostics, nameof(diagnostics));
            ArgumentNullException.ThrowIfNull(
                temporaryDirectory,
                nameof(temporaryDirectory));
            ArgumentNullException.ThrowIfNull(
                applicationManifest,
                nameof(applicationManifest));
            ArgumentNullException.ThrowIfNull(payloads, nameof(payloads));
            ArgumentNullException.ThrowIfNull(
                adjacentExecutables,
                nameof(adjacentExecutables));
            ArgumentNullException.ThrowIfNull(
                originalResolvedPaths,
                nameof(originalResolvedPaths));

            ClickOnceStagedFile[] stagedFiles = GetStagedFiles(
                deploymentManifest,
                applicationManifest,
                payloads,
                adjacentExecutables).ToArray();

            _diagnostics = diagnostics.ToArray();
            _temporaryDirectory = temporaryDirectory;
            _originalResolvedPaths = originalResolvedPaths;
            _mappedFiles = stagedFiles
                .Where(file => file.FileInfoUpdateFile is not null)
                .GroupBy(
                    file => new MappedFilePath(
                        file.Destination.FullName,
                        file.FileInfoUpdateFile!.FullName),
                    MappedFilePathComparer.Instance)
                .Select(group => new MappedFile(group))
                .ToArray();

            DeploymentManifest = deploymentManifest;
            ApplicationManifest = applicationManifest;
            Payloads = new Dictionary<
                ResolvedClickOncePayload,
                ClickOnceStagedFile>(payloads);
            AdjacentExecutables = new Dictionary<
                ResolvedClickOnceAdjacentExecutable,
                ClickOnceStagedFile>(adjacentExecutables);
        }

        internal DirectoryInfo Directory => _temporaryDirectory.Directory;
        internal ClickOnceStagedFile? DeploymentManifest { get; }
        internal ClickOnceStagedFile ApplicationManifest { get; }
        internal IReadOnlyDictionary<
            ResolvedClickOncePayload,
            ClickOnceStagedFile> Payloads { get; }
        internal IReadOnlyDictionary<
            ResolvedClickOnceAdjacentExecutable,
            ClickOnceStagedFile> AdjacentExecutables { get; }
        internal IReadOnlyList<ClickOnceManifestDiagnostic> Diagnostics =>
            _diagnostics;

        internal IDisposable BeginManifestFileInfoUpdate()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_manifestFileInfoUpdateActive)
            {
                throw new InvalidOperationException();
            }

            List<MappedFile> movedFiles = new(_mappedFiles.Count);
            MappedFile? currentFile = null;

            try
            {
                foreach (MappedFile mappedFile in _mappedFiles)
                {
                    currentFile = mappedFile;
                    File.Move(
                        mappedFile.StagedFile.Destination.FullName,
                        mappedFile.UpdateFile.FullName);
                    mappedFile.IsAtUpdatePath = true;
                    mappedFile.SetResolvedPath(mappedFile.UpdateFile.FullName);
                    movedFiles.Add(mappedFile);
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                IReadOnlyList<Exception> rollbackExceptions =
                    RestoreMappedFiles(movedFiles);
                _manifestFileInfoUpdateActive =
                    _mappedFiles.Any(file => file.IsAtUpdatePath);
                Exception innerException = rollbackExceptions.Count == 0
                    ? exception
                    : new AggregateException(
                        new[] { exception }.Concat(rollbackExceptions));

                throw CreateMappedSuffixException(
                    currentFile!,
                    innerException);
            }

            _manifestFileInfoUpdateActive = true;

            return new ManifestFileInfoUpdateScope(owner: this);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Exception? restoreException = null;

            if (_manifestFileInfoUpdateActive)
            {
                try
                {
                    EndManifestFileInfoUpdate();
                }
                catch (Exception exception)
                {
                    restoreException = exception;
                }
            }

            foreach (
                KeyValuePair<BaseReference, string?> pair
                in _originalResolvedPaths)
            {
                pair.Key.ResolvedPath = pair.Value;
            }

            _temporaryDirectory.Dispose();
            _disposed = true;

            if (restoreException is not null)
            {
                throw restoreException;
            }
        }

        private void EndManifestFileInfoUpdate()
        {
            if (_disposed || !_manifestFileInfoUpdateActive)
            {
                return;
            }

            List<ClickOnceStagingException> exceptions = new();

            foreach (MappedFile mappedFile in _mappedFiles.Reverse())
            {
                if (!mappedFile.IsAtUpdatePath)
                {
                    continue;
                }

                try
                {
                    File.Move(
                        mappedFile.UpdateFile.FullName,
                        mappedFile.StagedFile.Destination.FullName);
                    mappedFile.IsAtUpdatePath = false;
                    mappedFile.SetResolvedPath(
                        mappedFile.StagedFile.Destination.FullName);
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException)
                {
                    exceptions.Add(
                        CreateMappedSuffixException(
                            mappedFile,
                            exception));
                }
            }

            _manifestFileInfoUpdateActive =
                _mappedFiles.Any(file => file.IsAtUpdatePath);
            ThrowRestoreFailures(exceptions);
        }

        private IReadOnlyList<Exception> RestoreMappedFiles(
            IEnumerable<MappedFile> movedFiles)
        {
            List<Exception> exceptions = new();

            foreach (MappedFile mappedFile in movedFiles.Reverse())
            {
                try
                {
                    File.Move(
                        mappedFile.UpdateFile.FullName,
                        mappedFile.StagedFile.Destination.FullName);
                    mappedFile.IsAtUpdatePath = false;
                    mappedFile.SetResolvedPath(
                        mappedFile.StagedFile.Destination.FullName);
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException)
                {
                    exceptions.Add(
                        CreateMappedSuffixException(
                            mappedFile,
                            exception));
                }
            }

            return exceptions;
        }

        private void ThrowRestoreFailures(
            IReadOnlyList<ClickOnceStagingException> exceptions)
        {
            if (exceptions.Count == 0)
            {
                return;
            }

            if (exceptions.Count == 1)
            {
                throw exceptions[0];
            }

            ClickOnceStagingException first = exceptions[0];

            throw new ClickOnceStagingException(
                first.Message,
                Diagnostics,
                new AggregateException(exceptions));
        }

        private ClickOnceStagingException CreateMappedSuffixException(
            MappedFile mappedFile,
            Exception innerException)
        {
            return new ClickOnceStagingException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceStagingMappedSuffixFailed,
                    mappedFile.StagedFile.TargetPath),
                Diagnostics,
                innerException);
        }

        private sealed class ManifestFileInfoUpdateScope : IDisposable
        {
            private ClickOnceStagingSession? _owner;

            internal ManifestFileInfoUpdateScope(ClickOnceStagingSession owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                ClickOnceStagingSession? owner =
                    Interlocked.Exchange(ref _owner, value: null);

                owner?.EndManifestFileInfoUpdate();
            }
        }

        private sealed class MappedFile
        {
            private readonly IReadOnlyList<BaseReference> _manifestReferences;

            internal MappedFile(IEnumerable<ClickOnceStagedFile> stagedFiles)
            {
                ClickOnceStagedFile[] files = stagedFiles.ToArray();

                StagedFile = files[0];
                UpdateFile = StagedFile.FileInfoUpdateFile!;
                _manifestReferences = files
                    .Select(file => file.ManifestReference!)
                    .Distinct<BaseReference>(ReferenceEqualityComparer.Instance)
                    .ToArray();
            }

            internal ClickOnceStagedFile StagedFile { get; }
            internal FileInfo UpdateFile { get; }
            internal bool IsAtUpdatePath { get; set; }

            internal void SetResolvedPath(string path)
            {
                foreach (BaseReference reference in _manifestReferences)
                {
                    reference.ResolvedPath = path;
                }
            }
        }

        private sealed class MappedFilePathComparer :
            IEqualityComparer<MappedFilePath>
        {
            internal static readonly MappedFilePathComparer Instance = new();

            public bool Equals(MappedFilePath left, MappedFilePath right)
            {
                return string.Equals(
                        left.StablePath,
                        right.StablePath,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        left.UpdatePath,
                        right.UpdatePath,
                        StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(MappedFilePath value)
            {
                return HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(
                        value.StablePath),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(
                        value.UpdatePath));
            }
        }

        private readonly record struct MappedFilePath(
            string StablePath,
            string UpdatePath);

        private static IEnumerable<ClickOnceStagedFile> GetStagedFiles(
            ClickOnceStagedFile? deploymentManifest,
            ClickOnceStagedFile applicationManifest,
            IReadOnlyDictionary<
                ResolvedClickOncePayload,
                ClickOnceStagedFile> payloads,
            IReadOnlyDictionary<
                ResolvedClickOnceAdjacentExecutable,
                ClickOnceStagedFile> adjacentExecutables)
        {
            if (deploymentManifest is not null)
            {
                yield return deploymentManifest;
            }

            yield return applicationManifest;

            foreach (ClickOnceStagedFile payload in payloads.Values)
            {
                yield return payload;
            }

            foreach (
                ClickOnceStagedFile adjacentExecutable
                in adjacentExecutables.Values)
            {
                yield return adjacentExecutable;
            }
        }
    }
}
