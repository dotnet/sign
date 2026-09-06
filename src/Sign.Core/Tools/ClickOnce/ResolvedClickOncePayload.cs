// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ResolvedClickOncePayload
    {
        private const string DeploySuffix = ".deploy";

        internal ResolvedClickOncePayload(
            FileInfo source,
            BaseReference reference)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(source));
            ArgumentNullException.ThrowIfNull(reference, nameof(reference));
            ArgumentException.ThrowIfNullOrWhiteSpace(
                reference.TargetPath,
                nameof(reference));

            string targetPath = reference.TargetPath;
            string targetFileName = Path.GetFileName(targetPath);

            if (string.IsNullOrWhiteSpace(targetFileName))
            {
                throw new ArgumentException(
                    "The reference target path must identify a file.",
                    nameof(reference));
            }

            bool isFileExtensionMapped;

            if (string.Equals(
                source.Name,
                targetFileName,
                StringComparison.OrdinalIgnoreCase))
            {
                isFileExtensionMapped = false;
            }
            else if (string.Equals(
                source.Name,
                $"{targetFileName}{DeploySuffix}",
                StringComparison.OrdinalIgnoreCase))
            {
                isFileExtensionMapped = true;
            }
            else
            {
                throw new ArgumentException(
                    "The source file name does not match the reference target path.",
                    nameof(source));
            }

            Source = source;
            Reference = reference;
            TargetPath = targetPath;
            IsFileExtensionMapped = isFileExtensionMapped;
        }

        internal FileInfo Source { get; }
        internal string TargetPath { get; }
        internal BaseReference Reference { get; }
        internal bool IsFileExtensionMapped { get; }
    }
}
