// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal interface IClickOnceManifest
    {
        AssemblyIdentity AssemblyIdentity { get; }
        OutputMessageCollection OutputMessages { get; }
        bool ReadOnly { get; set; }

        void ResolveFiles(IReadOnlyList<DirectoryInfo> searchDirectories);
        void UpdateFileInfo();
        void Write(FileInfo file);
    }
}
