// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal interface IDataFormatSigner
    {
        bool RequiresSequentialCoordination => false;

        // Whether the staged file must keep its original file name, e.g. because
        // StageSigningDependencies reproduces the source directory around it.
        bool RequiresOriginalFileName => false;
        bool CanSign(FileInfo file);
        Task SignAsync(IEnumerable<FileInfo> files, SignOptions options);

        // Signs one coordinated container or sequentially coordinated file while preserving its source
        // identity. Other coordinated files are signed in batches through SignAsync(IEnumerable<FileInfo>, ...).
        Task SignAsync(
            SigningFile file,
            SignOptions options,
            SigningOperationCoordinator coordinator)
        {
            return SignAsync(new[] { file.File }, options);
        }

        // Some signature mechanisms (e.g. ClickOnce) require extra files alongside the file being signed.
        // We can't rely on the user specifying everything (and inputs are signed in parallel, so doing so
        // would need extra synchronization), so this copies the file's dependencies into the staging
        // directory before signing. The signing lifecycle stages the file itself; don't copy it here.
        void StageSigningDependencies(
            FileInfo source,
            DirectoryInfo stagingDirectory,
            SignOptions options)
        {
        }
    }
}