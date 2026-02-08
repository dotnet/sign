// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal interface IApplicationManifest
    {
        AssemblyReferenceCollection AssemblyReferences { get; }
        FileReferenceCollection FileReferences { get; }
        OutputMessageCollection OutputMessages { get; }
        bool ReadOnly { get; set; }

        void ResolveFiles(string[] searchPaths);
        void UpdateFileInfo();
    }
}