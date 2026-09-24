// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal interface IDataFormatSigner
    {
        bool CanSign(FileInfo file);
        Task SignAsync(IEnumerable<FileInfo> files, SignOptions options);

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

        // Copies the file's signed dependencies from staging to the output directory. The signing lifecycle
        // publishes the file itself after this returns, so the signed file wins if a result shares its
        // destination. Don't copy the file here or depend on it being present in the output directory.
        void CopySigningResults(
            FileInfo stagedFile,
            DirectoryInfo outputDirectory,
            SignOptions options)
        {
        }
    }
}