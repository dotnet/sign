// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SigningOperationArtifact : IDisposable
    {
        private IDisposable? _owner;

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
        }

        internal FileInfo PrimaryFile { get; }
        internal DirectoryInfo RootDirectory { get; }
        internal bool IncludeSiblings { get; }

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

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, value: null)?.Dispose();
        }
    }
}
