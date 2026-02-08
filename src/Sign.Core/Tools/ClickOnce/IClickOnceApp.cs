// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal interface IClickOnceApp
    {
        IApplicationManifest? ApplicationManifest { get; }
        FileInfo? ApplicationManifestFile { get; }
        IDeployManifest DeploymentManifest { get; }
        FileInfo DeploymentManifestFile { get; }

        IEnumerable<FileInfo> GetPayloadFiles();
    }
}