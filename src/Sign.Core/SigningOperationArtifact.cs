// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SigningOperationArtifact : IDisposable
    {
        private IDisposable? _owner;
        private readonly Func<IReadOnlyList<FileInfo>> _getFiles;

        private SigningOperationArtifact(
            FileInfo primaryFile,
            DirectoryInfo rootDirectory,
            bool includeSiblings,
            IDisposable? owner)
        {
            ArgumentNullException.ThrowIfNull(
                primaryFile,
                nameof(primaryFile));
            ArgumentNullException.ThrowIfNull(
                rootDirectory,
                nameof(rootDirectory));

            PrimaryFile = primaryFile;
            RootDirectory = rootDirectory;
            IncludeSiblings = includeSiblings;
            _owner = owner;
            _getFiles = includeSiblings
                ? () => rootDirectory.EnumerateFiles(
                    "*",
                    SearchOption.AllDirectories).ToArray()
                : () => new[] { primaryFile };
        }

        internal FileInfo PrimaryFile { get; }
        internal DirectoryInfo RootDirectory { get; }
        internal bool IncludeSiblings { get; }

        internal IReadOnlyList<FileInfo> GetFiles() => _getFiles();

        private SigningOperationArtifact(
            FileInfo primaryFile,
            DirectoryInfo rootDirectory,
            FileInfo[] files,
            IDisposable owner)
            : this(primaryFile, rootDirectory, includeSiblings: true, owner)
        {
            _getFiles = () => files;
        }

        internal static SigningOperationArtifact Single(
            FileInfo primaryFile)
        {
            ArgumentNullException.ThrowIfNull(
                primaryFile,
                nameof(primaryFile));

            return new SigningOperationArtifact(
                primaryFile,
                primaryFile.Directory!,
                includeSiblings: false,
                owner: null);
        }

        internal static SigningOperationArtifact Layout(
            FileInfo primaryFile,
            DirectoryInfo rootDirectory)
        {
            return new SigningOperationArtifact(
                primaryFile,
                rootDirectory,
                includeSiblings: true,
                owner: null);
        }

        internal static SigningOperationArtifact OwnedSingle(
            FileInfo primaryFile,
            IDisposable owner)
        {
            ArgumentNullException.ThrowIfNull(
                primaryFile,
                nameof(primaryFile));
            ArgumentNullException.ThrowIfNull(owner, nameof(owner));

            return new SigningOperationArtifact(
                primaryFile,
                primaryFile.Directory!,
                includeSiblings: false,
                owner);
        }

        internal static SigningOperationArtifact OwnedLayout(
            FileInfo primaryFile,
            DirectoryInfo rootDirectory,
            IDisposable owner)
        {
            ArgumentNullException.ThrowIfNull(owner, nameof(owner));

            return new SigningOperationArtifact(
                primaryFile,
                rootDirectory,
                includeSiblings: true,
                owner);
        }

        // An application-manifest operation publishes only its intended
        // results, not excluded payloads copied into staging for hashing.
        internal static SigningOperationArtifact OwnedLayout(
            FileInfo primaryFile,
            DirectoryInfo rootDirectory,
            IEnumerable<FileInfo> files,
            IDisposable owner)
        {
            ArgumentNullException.ThrowIfNull(primaryFile, nameof(primaryFile));
            ArgumentNullException.ThrowIfNull(rootDirectory, nameof(rootDirectory));
            ArgumentNullException.ThrowIfNull(files, nameof(files));
            ArgumentNullException.ThrowIfNull(owner, nameof(owner));

            FileInfo[] selectedFiles = files.ToArray();
            string rootPath = PathContainment.GetFullDirectoryPath(rootDirectory.FullName);
            foreach (FileInfo file in selectedFiles)
            {
                if (file is null || !PathContainment.IsContained(rootPath, file.FullName))
                {
                    throw new ArgumentException(
                        "Every selected layout file must be inside its root directory.",
                        nameof(files));
                }
            }

            selectedFiles = selectedFiles.Distinct(FileInfoComparer.Instance).ToArray();

            if (!selectedFiles.Contains(primaryFile, FileInfoComparer.Instance))
            {
                throw new ArgumentException(
                    "A layout must include its primary file.",
                    nameof(files));
            }

            return new SigningOperationArtifact(
                primaryFile,
                rootDirectory,
                selectedFiles,
                owner);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, value: null)?.Dispose();
        }
    }
}
