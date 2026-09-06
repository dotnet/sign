// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ClickOnceStagedFile
    {
        internal ClickOnceStagedFile(
            FileInfo source,
            FileInfo destination,
            FileInfo? fileInfoUpdateFile,
            string targetPath,
            BaseReference? manifestReference)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(source));
            ArgumentNullException.ThrowIfNull(
                destination,
                nameof(destination));
            ArgumentException.ThrowIfNullOrWhiteSpace(
                targetPath,
                nameof(targetPath));

            Source = source;
            Destination = destination;
            FileInfoUpdateFile = fileInfoUpdateFile;
            TargetPath = targetPath;
            ManifestReference = manifestReference;
        }

        internal FileInfo Source { get; }
        internal FileInfo Destination { get; }
        internal FileInfo? FileInfoUpdateFile { get; }
        internal string TargetPath { get; }
        internal BaseReference? ManifestReference { get; }
    }
}
