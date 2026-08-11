// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ClickOnceStagingResult : IDisposable
    {
        private readonly ClickOnceFileGraph _graph;
        private readonly
            IReadOnlyDictionary<BaseReference, string?> _originalResolvedPaths;
        private readonly IReadOnlyList<MappedFile> _mappedFiles;
        private readonly TemporaryDirectory _temporaryDirectory;
        private bool _disposed;
        private bool _manifestUpdateActive;

        internal ClickOnceStagingResult(
            ClickOnceFileGraph graph,
            ClickOnceSigningMode mode,
            TemporaryDirectory temporaryDirectory,
            IEnumerable<ClickOnceStagedFile> files,
            IReadOnlyDictionary<BaseReference, string?>
                originalResolvedPaths)
        {
            ArgumentNullException.ThrowIfNull(graph, nameof(graph));
            ArgumentNullException.ThrowIfNull(
                temporaryDirectory,
                nameof(temporaryDirectory));
            ArgumentNullException.ThrowIfNull(files, nameof(files));
            ArgumentNullException.ThrowIfNull(
                originalResolvedPaths,
                nameof(originalResolvedPaths));

            ClickOnceStagedFile[] stagedFiles = files.ToArray();

            _graph = graph;
            _temporaryDirectory = temporaryDirectory;
            _originalResolvedPaths = originalResolvedPaths;
            _mappedFiles = stagedFiles
                .Where(file => file.ManifestUpdateFile is not null)
                .GroupBy(
                    file => new MappedFilePath(
                        file.File.FullName,
                        file.ManifestUpdateFile!.FullName),
                    MappedFilePathComparer.Instance)
                .Select(group => new MappedFile(group))
                .ToArray();

            Mode = mode;
            Files = stagedFiles;
        }

        internal ClickOnceSigningMode Mode { get; }
        internal DirectoryInfo Directory => _temporaryDirectory.Directory;
        internal IReadOnlyList<ClickOnceStagedFile> Files { get; }
        internal IReadOnlyList<ClickOnceManifestDiagnostic> Diagnostics =>
            _graph.Diagnostics;

        internal IDisposable BeginManifestUpdate()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_manifestUpdateActive)
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
                        mappedFile.StagedFile.File.FullName,
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
                _manifestUpdateActive =
                    _mappedFiles.Any(file => file.IsAtUpdatePath);
                Exception innerException = rollbackExceptions.Count == 0
                    ? exception
                    : new AggregateException(
                        new[] { exception }.Concat(rollbackExceptions));

                throw CreateMappedSuffixException(
                    currentFile!,
                    innerException);
            }

            _manifestUpdateActive = true;

            return new ManifestUpdateScope(owner: this);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Exception? restoreException = null;

            if (_manifestUpdateActive)
            {
                try
                {
                    EndManifestUpdate();
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

        private void EndManifestUpdate()
        {
            if (_disposed || !_manifestUpdateActive)
            {
                return;
            }

            List<ClickOnceFileGraphStagingException> exceptions = new();

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
                        mappedFile.StagedFile.File.FullName);
                    mappedFile.IsAtUpdatePath = false;
                    mappedFile.SetResolvedPath(
                        mappedFile.StagedFile.File.FullName);
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

            _manifestUpdateActive =
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
                        mappedFile.StagedFile.File.FullName);
                    mappedFile.IsAtUpdatePath = false;
                    mappedFile.SetResolvedPath(
                        mappedFile.StagedFile.File.FullName);
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
            IReadOnlyList<ClickOnceFileGraphStagingException> exceptions)
        {
            if (exceptions.Count == 0)
            {
                return;
            }

            if (exceptions.Count == 1)
            {
                throw exceptions[0];
            }

            ClickOnceFileGraphStagingException first = exceptions[0];

            throw new ClickOnceFileGraphStagingException(
                first.Message,
                Diagnostics,
                new AggregateException(exceptions));
        }

        private ClickOnceFileGraphStagingException CreateMappedSuffixException(
            MappedFile mappedFile,
            Exception innerException)
        {
            return new ClickOnceFileGraphStagingException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceStagingMappedSuffixFailed,
                    mappedFile.StagedFile.GraphEntry.TargetPath),
                Diagnostics,
                innerException);
        }

        private sealed class ManifestUpdateScope : IDisposable
        {
            private ClickOnceStagingResult? _owner;

            internal ManifestUpdateScope(ClickOnceStagingResult owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                ClickOnceStagingResult? owner =
                    Interlocked.Exchange(ref _owner, value: null);

                owner?.EndManifestUpdate();
            }
        }

        private sealed class MappedFile
        {
            private readonly IReadOnlyList<BaseReference> _manifestReferences;

            internal MappedFile(IEnumerable<ClickOnceStagedFile> stagedFiles)
            {
                ClickOnceStagedFile[] files = stagedFiles.ToArray();

                StagedFile = files[0];
                UpdateFile = StagedFile.ManifestUpdateFile!;
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
    }
}
