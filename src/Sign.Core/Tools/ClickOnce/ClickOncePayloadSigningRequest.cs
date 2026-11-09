// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class ClickOncePayloadSigningRequest
    {
        internal ClickOncePayloadSigningRequest(
            SigningFile source,
            DirectoryInfo sourceRoot,
            DirectoryInfo stagingRoot,
            ClickOnceStagedFile stagedFile)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(sourceRoot);
            ArgumentNullException.ThrowIfNull(stagingRoot);
            ArgumentNullException.ThrowIfNull(stagedFile);

            Source = source;
            SourceRoot = sourceRoot;
            StagingRoot = stagingRoot;
            StagedFile = stagedFile;
        }

        internal SigningFile Source { get; }
        internal DirectoryInfo SourceRoot { get; }
        internal DirectoryInfo StagingRoot { get; }
        internal ClickOnceStagedFile StagedFile { get; }
        internal FileInfo UpdateFile => StagedFile.FileInfoUpdateFile ?? StagedFile.Destination;
    }
}
