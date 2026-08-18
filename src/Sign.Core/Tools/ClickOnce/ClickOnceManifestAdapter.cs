// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal abstract class ClickOnceManifestAdapter<TManifest> : IClickOnceManifest
        where TManifest : Manifest
    {
        private const string TargetFrameworkVersion = "v4.5";

        protected TManifest Manifest { get; }

        protected ClickOnceManifestAdapter(TManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest, nameof(manifest));

            Manifest = manifest;
        }

        public AssemblyIdentity AssemblyIdentity => Manifest.AssemblyIdentity;
        public AssemblyReferenceCollection AssemblyReferences => Manifest.AssemblyReferences;
        public IReadOnlyList<ClickOnceManifestDiagnostic> Diagnostics => Manifest.OutputMessages
            .Cast<OutputMessage>()
            .Select(message => new ClickOnceManifestDiagnostic(message))
            .ToArray();
        public AssemblyReference? EntryPoint => Manifest.EntryPoint;
        public FileReferenceCollection FileReferences => Manifest.FileReferences;

        public bool ReadOnly
        {
            get => Manifest.ReadOnly;
            set => Manifest.ReadOnly = value;
        }

        public void ResolveFiles(IReadOnlyList<DirectoryInfo> searchDirectories)
        {
            ArgumentNullException.ThrowIfNull(
                searchDirectories,
                nameof(searchDirectories));

            string[] searchPaths = new string[searchDirectories.Count];

            for (int i = 0; i < searchDirectories.Count; ++i)
            {
                DirectoryInfo? searchDirectory = searchDirectories[i];

                if (searchDirectory is null)
                {
                    throw new ArgumentException(
                        message: null,
                        nameof(searchDirectories));
                }

                searchPaths[i] = searchDirectory.FullName;
            }

            Manifest.ResolveFiles(searchPaths);
        }

        public void UpdateFileInfo()
        {
            Manifest.UpdateFileInfo(TargetFrameworkVersion);
        }

        public void Write(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            if (Manifest.InputStream is { CanSeek: true } inputStream)
            {
                inputStream.Position = 0;
            }

            ManifestWriter.WriteManifest(
                Manifest,
                file.FullName,
                TargetFrameworkVersion);
        }
    }
}
