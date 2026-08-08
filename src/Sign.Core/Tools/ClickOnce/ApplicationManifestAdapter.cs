// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ApplicationManifestAdapter : IApplicationManifest
    {
        private readonly ApplicationManifest _manifest;

        internal ApplicationManifestAdapter(ApplicationManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest, nameof(manifest));

            _manifest = manifest;
        }

        public AssemblyIdentity AssemblyIdentity => _manifest.AssemblyIdentity;
        public AssemblyReferenceCollection AssemblyReferences => _manifest.AssemblyReferences;
        public AssemblyReference? EntryPoint => _manifest.EntryPoint;
        public FileReferenceCollection FileReferences => _manifest.FileReferences;
        public OutputMessageCollection OutputMessages => _manifest.OutputMessages;

        public bool ReadOnly
        {
            get => _manifest.ReadOnly;
            set => _manifest.ReadOnly = value;
        }

        public void ResolveFiles(string[] searchPaths)
        {
            ArgumentNullException.ThrowIfNull(searchPaths, nameof(searchPaths));

            _manifest.ResolveFiles(searchPaths);
        }

        public void UpdateFileInfo(string targetFrameworkVersion)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetFrameworkVersion, nameof(targetFrameworkVersion));

            _manifest.UpdateFileInfo(targetFrameworkVersion);
        }

        public void Write(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream, nameof(stream));

            ManifestWriter.WriteManifest(_manifest, stream);
        }
    }
}
