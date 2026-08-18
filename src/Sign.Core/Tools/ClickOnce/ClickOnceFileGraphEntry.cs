// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ClickOnceFileGraphEntry
    {
        internal ClickOnceFileGraphEntry(
            FileInfo source,
            string targetPath,
            ClickOnceFileGraphEntryKind kind,
            BaseReference? manifestReference = null,
            string? mappingAddedSuffix = null)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(source));
            ArgumentException.ThrowIfNullOrWhiteSpace(targetPath, nameof(targetPath));

            Source = source;
            TargetPath = targetPath;
            Kind = kind;
            ManifestReference = manifestReference;
            MappingAddedSuffix = mappingAddedSuffix;
        }

        internal FileInfo Source { get; }
        internal string TargetPath { get; }
        internal ClickOnceFileGraphEntryKind Kind { get; }
        internal BaseReference? ManifestReference { get; }
        internal string? MappingAddedSuffix { get; }
    }
}
