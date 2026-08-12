// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;

namespace Sign.Core
{
    internal interface IClickOnceManifestReader
    {
        bool TryReadApplicationManifest(
            Stream stream,
            [NotNullWhen(true)] out IApplicationManifest? manifest);

        bool TryReadDeployManifest(
            Stream stream,
            [NotNullWhen(true)] out IDeployManifest? manifest);
    }
}
