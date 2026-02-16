// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ManifestReaderAdapter : IManifestReader
    {
        public ManifestReaderAdapter()
        {
            MsBuildLocatorInitializer.EnsureRegistered();
        }

        public Manifest? ReadManifest(Stream stream, bool preserveStream)
        {
            return ManifestReader.ReadManifest(stream, preserveStream);
        }

        public bool TryReadDeployManifest(Stream stream, [NotNullWhen(true)] out IDeployManifest? deployManifest)
        {
            deployManifest = null;

            Manifest? manifest = ManifestReader.ReadManifest(stream, preserveStream: true);

            if (manifest is DeployManifest dm)
            {
                dm.ReadOnly = false;
                deployManifest = new DeployManifestAdapter(dm);

                return true;
            }

            return false;
        }

        public bool TryReadApplicationManifest(Stream stream, [NotNullWhen(true)] out IApplicationManifest? applicationManifest)
        {
            applicationManifest = null;

            Manifest? manifest = ManifestReader.ReadManifest(stream, preserveStream: true);

            if (manifest is ApplicationManifest am)
            {
                am.ReadOnly = false;
                applicationManifest = new ApplicationManifestAdapter(am);

                return true;
            }

            return false;
        }
    }
}
