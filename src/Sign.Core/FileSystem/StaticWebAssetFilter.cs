// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    /// <summary>
    /// Excludes ASP.NET Core static web assets from signing.
    /// </summary>
    /// <remarks>
    /// Packages produced by the ASP.NET Core SDK (for example, Blazor and Razor class libraries) carry their
    /// browser assets in a <c>staticwebassets</c> directory and record each asset's integrity hash at pack time in
    /// <c>build/Microsoft.AspNetCore.StaticWebAssets.props</c>.  Signing such an asset --- most commonly a .js
    /// file, which is indistinguishable by extension from a Windows Script Host script --- changes the file and
    /// invalidates the recorded hash, which breaks consuming applications.
    /// See https://github.com/dotnet/sign/issues/1045.
    /// </remarks>
    internal sealed class StaticWebAssetFilter : IStaticWebAssetFilter
    {
        // Matches Microsoft.AspNetCore.StaticWebAssets.props and, for .NET 9 and later,
        // Microsoft.AspNetCore.StaticWebAssetEndpoints.props.
        private const string MarkerFileNamePrefix = "Microsoft.AspNetCore.StaticWebAsset";
        private const string MarkerFileNameExtension = ".props";
        private const string StaticWebAssetsDirectoryName = "staticwebassets";

        private static readonly HashSet<string> BuildDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "build",
            "buildMultiTargeting",
            "buildTransitive"
        };

        private readonly ILogger<IStaticWebAssetFilter> _logger;

        // Dependency injection requires a public constructor.
        public StaticWebAssetFilter(ILogger<IStaticWebAssetFilter> logger)
        {
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _logger = logger;
        }

        public IReadOnlyList<FileInfo> Filter(IEnumerable<FileInfo> files)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));

            List<FileInfo> allFiles = files.ToList();
            Dictionary<string, string> staticWebAssetDirectories = GetStaticWebAssetDirectories(allFiles);

            if (staticWebAssetDirectories.Count == 0)
            {
                return allFiles;
            }

            List<FileInfo> filesToSign = new(allFiles.Count);

            foreach (FileInfo file in allFiles)
            {
                string? markerFileName = GetMarkerFileName(file, staticWebAssetDirectories);

                if (markerFileName is null)
                {
                    filesToSign.Add(file);
                }
                else
                {
                    _logger.LogWarning(Resources.SkippingStaticWebAsset, file.FullName, markerFileName);
                }
            }

            return filesToSign;
        }

        /// <summary>
        /// Finds each static web assets directory implied by a marker file.
        /// </summary>
        /// <returns>A dictionary of static web assets directory path to the name of the marker file that
        /// identified it.</returns>
        private static Dictionary<string, string> GetStaticWebAssetDirectories(IReadOnlyList<FileInfo> files)
        {
            Dictionary<string, string> directories = new(StringComparer.OrdinalIgnoreCase);

            foreach (FileInfo file in files)
            {
                if (!IsMarkerFile(file))
                {
                    continue;
                }

                // The marker file is <package root>/build/Microsoft.AspNetCore.StaticWebAssets.props,
                // and the assets it describes are under <package root>/staticwebassets.
                DirectoryInfo? packageRootDirectory = file.Directory?.Parent;

                if (packageRootDirectory is null)
                {
                    continue;
                }

                string staticWebAssetsDirectoryPath = Path.Combine(
                    packageRootDirectory.FullName,
                    StaticWebAssetsDirectoryName);

                directories[staticWebAssetsDirectoryPath] = file.Name;
            }

            return directories;
        }

        private static bool IsMarkerFile(FileInfo file)
        {
            return file.Name.StartsWith(MarkerFileNamePrefix, StringComparison.OrdinalIgnoreCase)
                && file.Name.EndsWith(MarkerFileNameExtension, StringComparison.OrdinalIgnoreCase)
                && file.Directory is not null
                && BuildDirectoryNames.Contains(file.Directory.Name);
        }

        /// <summary>
        /// Gets the name of the marker file identifying <paramref name="file" /> as a static web asset,
        /// or <c>null</c> if the file is not a static web asset.
        /// </summary>
        private static string? GetMarkerFileName(FileInfo file, Dictionary<string, string> staticWebAssetDirectories)
        {
            foreach (KeyValuePair<string, string> staticWebAssetDirectory in staticWebAssetDirectories)
            {
                string directoryPathPrefix = staticWebAssetDirectory.Key + Path.DirectorySeparatorChar;

                if (file.FullName.StartsWith(directoryPathPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return staticWebAssetDirectory.Value;
                }
            }

            return null;
        }
    }
}
