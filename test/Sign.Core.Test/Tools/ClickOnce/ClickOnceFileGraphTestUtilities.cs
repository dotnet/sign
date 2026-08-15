// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core.Test
{
    internal static class ClickOnceFileGraphTestUtilities
    {
        internal const string ManifestVersion = "1.0.0.0";
        internal const string ProcessorArchitecture = "msil";

        internal static ApplicationManifest CreateApplicationManifest()
        {
            ApplicationManifest manifest = new();
            manifest.AssemblyIdentity.Name = "TestApplication";
            manifest.AssemblyIdentity.Version = ManifestVersion;
            manifest.AssemblyIdentity.ProcessorArchitecture = ProcessorArchitecture;

            return manifest;
        }

        internal static FileInfo CreateFile(
            DirectoryInfo root,
            string relativePath,
            string contents = "payload")
        {
            FileInfo file = new(Path.Combine(root.FullName, relativePath));
            file.Directory!.Create();
            File.WriteAllText(file.FullName, contents);
            file.Refresh();

            return file;
        }

        internal static FileInfo WriteApplicationManifest(
            DirectoryInfo root,
            string relativePath,
            params string[] payloadTargetPaths)
        {
            ApplicationManifest manifest = CreateApplicationManifest();

            foreach (string targetPath in payloadTargetPaths)
            {
                manifest.FileReferences.Add(
                    new FileReference()
                    {
                        TargetPath = targetPath
                    });
            }

            return WriteManifest(root, relativePath, manifest);
        }

        internal static FileInfo WriteDeploymentManifest(
            DirectoryInfo root,
            string relativePath,
            string applicationManifestTargetPath,
            bool mapFileExtensions = false)
        {
            DeployManifest manifest = new()
            {
                MapFileExtensions = mapFileExtensions
            };
            manifest.AssemblyIdentity.Name = "TestDeployment";
            manifest.AssemblyIdentity.Version = ManifestVersion;
            manifest.AssemblyIdentity.ProcessorArchitecture = ProcessorArchitecture;

            AssemblyReference entryPoint = new(applicationManifestTargetPath);

            manifest.AssemblyReferences.Add(entryPoint);
            manifest.EntryPoint = entryPoint;

            return WriteManifest(root, relativePath, manifest);
        }

        internal static FileInfo WriteManifest(
            DirectoryInfo root,
            string relativePath,
            Manifest manifest)
        {
            FileInfo file = new(Path.Combine(root.FullName, relativePath));
            file.Directory!.Create();

            using (FileStream stream = file.Create())
            {
                ManifestWriter.WriteManifest(manifest, stream);
            }

            file.Refresh();

            return file;
        }

        internal static FileInfo WriteMalformedManifest(
            DirectoryInfo root,
            string relativePath)
        {
            const string Xml = "<assembly>";

            return CreateFile(root, relativePath, Xml);
        }

        internal static string GetVstoManifestPath()
        {
            return Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "ClickOnce",
                "VstoApplication.manifest");
        }
    }
}
