// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal interface IManifestReader
    {
        Manifest? ReadManifest(Stream stream, bool preserveStream);
        bool TryReadDeployManifest(Stream stream, [NotNullWhen(true)] out IDeployManifest? deployManifest);
        bool TryReadApplicationManifest(Stream stream, [NotNullWhen(true)] out IApplicationManifest? applicationManifest);
    }
}