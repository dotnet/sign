// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Xml;

namespace Sign.TestInfrastructure
{
    internal static class TestTools
    {
        private static readonly Lazy<FileInfo> _vsWhereExe = new(FindVsWhereExe);
        private static readonly Lazy<DirectoryInfo> _repositoryDirectory = new(GetRepositoryDirectory);
        private static readonly Lazy<FileInfo> _directoryPackagesPropsFile = new(GetDirectoryPackagesPropsFile);

        internal static FileInfo GetVsWhereExe()
        {
            return _vsWhereExe.Value;
        }

        private static FileInfo FindVsWhereExe()
        {
            string? nugetPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");

            if (string.IsNullOrWhiteSpace(nugetPackages))
            {
                throw new InvalidOperationException("NUGET_PACKAGES environment variable is not set.");
            }

            // Parse NuGet.config to find the vswhere package version
            XmlDocument doc = new();

            doc.Load(_directoryPackagesPropsFile.Value.FullName);

            string? vswhereVersion = null;
            XmlNodeList? packageNodes = doc.SelectNodes("//Project/ItemGroup/PackageVersion[@Include='vswhere']");

            if (packageNodes is not null)
            {
                foreach (XmlNode node in packageNodes)
                {
                    XmlAttribute? versionAttr = node.Attributes?["Version"];

                    if (versionAttr is not null)
                    {
                        vswhereVersion = versionAttr.Value;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(vswhereVersion))
            {
                throw new InvalidOperationException("vswhere package version not found in Directory.Packages.props.");
            }

            // Compose the path to vswhere.exe in the global packages folder
            string vswhereExePath = Path.Combine(
                nugetPackages,
                "vswhere",
                vswhereVersion,
                "tools",
                "vswhere.exe");

            if (!File.Exists(vswhereExePath))
            {
                throw new FileNotFoundException("vswhere.exe not found at the expected location.", vswhereExePath);
            }

            return new FileInfo(vswhereExePath);
        }

        private static FileInfo GetDirectoryPackagesPropsFile()
        {
            string configPath = Path.Combine(_repositoryDirectory.Value.FullName, "Directory.Packages.props");
            configPath = Path.GetFullPath(configPath);

            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("Directory.Packages.props not found.", configPath);
            }

            return new FileInfo(configPath);
        }

        private static DirectoryInfo GetRepositoryDirectory()
        {
            string directoryPath = AppContext.BaseDirectory;
            DirectoryInfo? directory = new(directoryPath);

            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                directory = directory.Parent;
            }

            if (directory is null)
            {
                throw new DirectoryNotFoundException("Repository root not found.");
            }

            return directory;
        }
    }
}
