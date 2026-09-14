// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ClickOnceStagingPlanEntry
    {
        internal ClickOnceStagingPlanEntry(
            FileInfo source,
            FileInfo destination,
            FileInfo? fileInfoUpdateDestination,
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
            FileInfoUpdateDestination = fileInfoUpdateDestination;
            TargetPath = targetPath;
            ManifestReference = manifestReference;
        }

        internal FileInfo Source { get; }
        internal FileInfo Destination { get; }
        internal FileInfo? FileInfoUpdateDestination { get; }
        internal string TargetPath { get; }
        internal BaseReference? ManifestReference { get; }
    }
}
