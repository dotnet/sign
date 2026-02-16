// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class DeployManifestAdapter : IDeployManifest
    {
        private readonly DeployManifest _deployManifest;

        public AssemblyReference? EntryPoint => _deployManifest.EntryPoint;
        public bool MapFileExtensions => _deployManifest.MapFileExtensions;
        public OutputMessageCollection OutputMessages => _deployManifest.OutputMessages;
        public DeployManifest UnderlyingManifest => _deployManifest;

        public bool ReadOnly
        {
            get => _deployManifest.ReadOnly;
            set => _deployManifest.ReadOnly = value;
        }

        public DeployManifestAdapter(DeployManifest deployManifest)
        {
            ArgumentNullException.ThrowIfNull(deployManifest, nameof(deployManifest));

            _deployManifest = deployManifest;
        }

        public void ResolveFiles(string[] searchPaths)
        {
            _deployManifest.ResolveFiles(searchPaths);
        }

        public void UpdateFileInfo()
        {
            _deployManifest.UpdateFileInfo();
        }
    }
}