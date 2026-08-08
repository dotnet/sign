// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ClickOnceManifestReader : IClickOnceManifestReader
    {
        public bool TryReadApplicationManifest(
            Stream stream,
            bool preserveStream,
            [NotNullWhen(true)] out IApplicationManifest? manifest)
        {
            ArgumentNullException.ThrowIfNull(stream, nameof(stream));

            Manifest? readManifest = ManifestReader.ReadManifest(stream, preserveStream);

            if (readManifest is ApplicationManifest applicationManifest)
            {
                manifest = new ApplicationManifestAdapter(applicationManifest);

                return true;
            }

            manifest = null;

            return false;
        }

        public bool TryReadDeployManifest(
            Stream stream,
            bool preserveStream,
            [NotNullWhen(true)] out IDeployManifest? manifest)
        {
            ArgumentNullException.ThrowIfNull(stream, nameof(stream));

            Manifest? readManifest = ManifestReader.ReadManifest(stream, preserveStream);

            if (readManifest is DeployManifest deployManifest)
            {
                manifest = new DeployManifestAdapter(deployManifest);

                return true;
            }

            manifest = null;

            return false;
        }
    }
}
