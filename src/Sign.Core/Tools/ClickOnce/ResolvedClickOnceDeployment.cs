// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ResolvedClickOnceDeployment
    {
        internal ResolvedClickOnceDeployment(
            FileInfo source,
            IDeployManifest manifest,
            AssemblyReference applicationManifestReference)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(source));
            ArgumentNullException.ThrowIfNull(manifest, nameof(manifest));
            ArgumentNullException.ThrowIfNull(
                applicationManifestReference,
                nameof(applicationManifestReference));

            if (!ReferenceEquals(
                manifest.EntryPoint,
                applicationManifestReference))
            {
                throw new ArgumentException(
                    "The application manifest reference must be the deployment manifest entry point.",
                    nameof(applicationManifestReference));
            }

            Source = source;
            Manifest = manifest;
            ApplicationManifestReference = applicationManifestReference;
        }

        internal FileInfo Source { get; }
        internal IDeployManifest Manifest { get; }
        internal AssemblyReference ApplicationManifestReference { get; }
    }
}
