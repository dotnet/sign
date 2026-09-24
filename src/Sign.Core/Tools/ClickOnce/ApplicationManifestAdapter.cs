// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ApplicationManifestAdapter :
        ClickOnceManifestAdapter<ApplicationManifest>,
        IApplicationManifest
    {
        internal ApplicationManifestAdapter(ApplicationManifest manifest)
            : base(manifest)
        {
        }
    }
}
