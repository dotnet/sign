// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ApplicationManifestAdapter : IApplicationManifest
    {
        private readonly ApplicationManifest _applicationManifest;

        public AssemblyReferenceCollection AssemblyReferences => _applicationManifest.AssemblyReferences;
        public FileReferenceCollection FileReferences => _applicationManifest.FileReferences;
        public OutputMessageCollection OutputMessages => _applicationManifest.OutputMessages;

        public bool ReadOnly
        {
            get => _applicationManifest.ReadOnly;
            set => _applicationManifest.ReadOnly = value;
        }
        public ApplicationManifest UnderlyingManifest => _applicationManifest;

        public ApplicationManifestAdapter(ApplicationManifest applicationManifest)
        {
            ArgumentNullException.ThrowIfNull(applicationManifest, nameof(applicationManifest));
            _applicationManifest = applicationManifest;
        }

        public void ResolveFiles(string[] searchPaths)
        {
            _applicationManifest.ResolveFiles(searchPaths);
        }

        public void UpdateFileInfo()
        {
            _applicationManifest.UpdateFileInfo();
        }
    }
}