// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    internal sealed class AggregatingSignerSpy : IAggregatingDataFormatSigner
    {
        internal List<FileInfo> FilesSubmittedForSigning { get; } = new();
        internal List<SigningSourceIdentity> SourceIdentities { get; } =
            new();

        public bool CanSign(FileInfo file)
        {
            throw new NotImplementedException();
        }

        public bool HasSigningWork(FileInfo file, SignOptions options)
        {
            return true;
        }

        public bool IsSequentialCoordinationRequired(FileInfo file)
        {
            return false;
        }

        public bool IsOriginalFileNameRequired(FileInfo file)
        {
            return false;
        }

        public Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            FilesSubmittedForSigning.AddRange(files);

            return Task.CompletedTask;
        }

        public Task SignAsync(
            IEnumerable<SigningFile> files,
            SignOptions options,
            SigningOperationCoordinator coordinator)
        {
            List<SigningFile> signingFiles = files.ToList();

            FilesSubmittedForSigning.AddRange(
                signingFiles.Select(file => file.File));
            SourceIdentities.AddRange(
                signingFiles.Select(file => file.SourceIdentity));

            return Task.CompletedTask;
        }

        public Task SignOwnerAsync(
            SigningFile file,
            SignOptions options,
            SigningOperationCoordinator coordinator)
        {
            FilesSubmittedForSigning.Add(file.File);
            SourceIdentities.Add(file.SourceIdentity);

            return Task.CompletedTask;
        }
    }
}