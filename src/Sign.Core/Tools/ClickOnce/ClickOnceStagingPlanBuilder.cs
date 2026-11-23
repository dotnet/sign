// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ClickOnceStagingPlanBuilder
    {
        internal ClickOnceStagingPlan Build(
            ResolvedClickOncePublishLayout layout,
            DirectoryInfo stagingDirectory,
            IReadOnlyDictionary<ResolvedClickOncePayload, FileInfo>? originalSources = null)
        {
            ArgumentNullException.ThrowIfNull(layout, nameof(layout));
            ArgumentNullException.ThrowIfNull(
                stagingDirectory,
                nameof(stagingDirectory));

            string rootPath = PathContainment.GetFullDirectoryPath(
                stagingDirectory.FullName);
            DestinationAllocator allocator = new(layout, rootPath);
            Dictionary<BaseReference, AllocatedStagingPlanEntry>
                referenceDestinations =
                    new(ReferenceEqualityComparer.Instance);
            List<AllocatedStagingPlanEntry> entries = new();

            AllocatedStagingPlanEntry Allocate(
                string basePath,
                StagingSource source)
            {
                AllocatedStagingPlanEntry entry = allocator.Allocate(
                    basePath,
                    source);
                AddReferenceDestination(
                    layout,
                    referenceDestinations,
                    entry);
                entries.Add(entry);

                return entry;
            }

            AllocatedStagingPlanEntry? deploymentManifest = null;

            if (layout.Deployment is ResolvedClickOnceDeployment deployment)
            {
                deploymentManifest = Allocate(
                    rootPath,
                    new StagingSource(
                        deployment.Source,
                        deployment.Source.Name,
                        manifestReference: null,
                        mappingAddedSuffix: null));
            }

            BaseReference? applicationManifestReference =
                layout.Deployment?.ApplicationManifestReference;
            AllocatedStagingPlanEntry applicationManifest = Allocate(
                rootPath,
                new StagingSource(
                    layout.Application.Source,
                    applicationManifestReference?.TargetPath ??
                        layout.Application.Source.Name,
                    applicationManifestReference,
                    mappingAddedSuffix: null));
            string applicationDirectoryPath =
                applicationManifest.Destination.Directory!.FullName;
            Dictionary<ResolvedClickOncePayload, AllocatedStagingPlanEntry>
                payloads = layout.Application.Payloads.ToDictionary(
                    payload => payload,
                    payload => Allocate(
                        applicationDirectoryPath,
                        new StagingSource(
                            originalSources is null ? payload.Source : originalSources[payload],
                            payload.TargetPath,
                            payload.Reference,
                            payload.IsFileExtensionMapped
                                ? ".deploy"
                                : null)));
            Dictionary<
                ResolvedClickOnceAdjacentExecutable,
                AllocatedStagingPlanEntry> adjacentExecutables =
                    layout.AdjacentExecutables.ToDictionary(
                        executable => executable,
                        executable => Allocate(
                            rootPath,
                            new StagingSource(
                                executable.Source,
                                executable.TargetPath,
                                manifestReference: null,
                                mappingAddedSuffix: null)));

            ValidateDestinations(layout, entries);
            if (originalSources is not null)
            {
                Dictionary<string, AllocatedStagingPlanEntry> sourceDestinations = new(StringComparer.OrdinalIgnoreCase);
                foreach (AllocatedStagingPlanEntry entry in payloads.Values)
                {
                    if (sourceDestinations.TryGetValue(entry.Destination.FullName,
                        out AllocatedStagingPlanEntry? existing) &&
                        !string.Equals(existing.Source.File.FullName, entry.Source.File.FullName, StringComparison.Ordinal))
                    {
                        throw CreateCollisionException(layout, existing, entry);
                    }
                    sourceDestinations[entry.Destination.FullName] = entry;
                }
            }

            return new ClickOnceStagingPlan(
                deploymentManifest?.Entry,
                applicationManifest.Entry,
                payloads.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Entry),
                adjacentExecutables.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Entry));
        }

        internal void ValidateAdditionalOutputs(
            ResolvedClickOncePublishLayout layout,
            ClickOnceStagingSession session,
            IReadOnlyList<SigningOperationPlan> outputs)
        {
            string rootPath = PathContainment.GetFullDirectoryPath(session.Directory.FullName);
            DestinationAllocator allocator = new(layout, rootPath);
            List<AllocatedStagingPlanEntry> entries = new();
            foreach (ClickOnceStagedFile file in session.Payloads.Values
                .Append(session.ApplicationManifest))
            {
                entries.Add(allocator.Allocate(
                    rootPath,
                    new StagingSource(
                        file.Source,
                        Path.GetRelativePath(
                            session.Directory.FullName,
                            (file.FileInfoUpdateFile ?? file.Destination).FullName),
                        file.ManifestReference,
                        file.FileInfoUpdateFile is null ? null : ".deploy")));
            }

            AllocatedStagingPlanEntry[] reserved = entries.ToArray();
            foreach (SigningOperationPlan output in outputs)
            {
                string destinationPath = output.Output.FullName;
                if (!PathContainment.IsContained(rootPath, destinationPath) ||
                    (!destinationPath.StartsWith(rootPath, StringComparison.Ordinal) &&
                        !ClickOncePathSpelling.IsSameDirectory(
                            new DirectoryInfo(Path.TrimEndingDirectorySeparator(rootPath)),
                            new DirectoryInfo(Path.TrimEndingDirectorySeparator(destinationPath[..rootPath.Length])))))
                {
                    throw CreateUnsafePathException(layout, new StagingSource(
                        output.Source.File, destinationPath, manifestReference: null, mappingAddedSuffix: null));
                }

                AllocatedStagingPlanEntry entry = allocator.Allocate(
                    rootPath,
                    new StagingSource(
                        output.Source.File,
                        Path.GetRelativePath(session.Directory.FullName, output.Output.FullName),
                        manifestReference: null,
                        mappingAddedSuffix: null));
                // Check the actual materialization path, not the allocator's
                // case-insensitive representative of a duplicate claim.
                entry = new AllocatedStagingPlanEntry(
                    entry.Source, output.Output, fileInfoUpdateDestination: null, entry.Claim);
                foreach (AllocatedStagingPlanEntry existing in reserved)
                {
                    if (PathsEqual(entry.Destination.FullName, existing.Destination.FullName) ||
                        (existing.FileInfoUpdateDestination is not null &&
                            PathsEqual(entry.Destination.FullName, existing.FileInfoUpdateDestination.FullName)))
                    {
                        throw CreateCollisionException(layout, existing, entry);
                    }
                }
                entries.Add(entry);
            }
            ValidateDestinations(layout, entries);
        }

        private static void ValidateDestinations(
            ResolvedClickOncePublishLayout layout,
            IReadOnlyList<AllocatedStagingPlanEntry> plan)
        {
            List<DestinationUse> destinations = new(plan.Count * 2); // Stable and optional update paths.

            foreach (AllocatedStagingPlanEntry entry in plan)
            {
                destinations.Add(
                    new DestinationUse(
                        entry.Destination.FullName,
                        entry,
                        false));

                if (entry.FileInfoUpdateDestination is not null)
                {
                    destinations.Add(
                        new DestinationUse(
                            entry.FileInfoUpdateDestination.FullName,
                            entry,
                            true));
                }
            }

            destinations.Sort(DestinationUseComparer.Instance);

            for (int index = 1; index < destinations.Count; ++index)
            {
                DestinationUse previous = destinations[index - 1];
                DestinationUse current = destinations[index];

                if (string.Equals(
                    previous.Path,
                    current.Path,
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (!AreReusableDuplicates(previous, current))
                    {
                        throw CreateCollisionException(
                            layout,
                            previous.Entry,
                            current.Entry);
                    }
                }
                else if (IsFileDirectoryCollision(
                    previous.Path,
                    current.Path))
                {
                    throw CreateCollisionException(
                        layout,
                        previous.Entry,
                        current.Entry);
                }
            }
        }

        private static bool AreReusableDuplicates(
            DestinationUse left,
            DestinationUse right)
        {
            return left.IsFileInfoUpdateDestination ==
                    right.IsFileInfoUpdateDestination &&
                DestinationClaimComparer.Instance.Equals(
                    left.Entry.Claim,
                    right.Entry.Claim) &&
                PathsEqual(
                    left.Entry.Source.File.FullName,
                    right.Entry.Source.File.FullName);
        }

        private static DestinationPaths? TryGetDirectDestinationPaths(
            string rootPath,
            string basePath,
            StagingSource source)
        {
            if (source.MappingAddedSuffix is string suffix)
            {
                string? stablePath = TryGetDirectDestinationPath(
                    rootPath,
                    basePath,
                    source.TargetPath + suffix);
                string? updatePath = TryGetDirectDestinationPath(
                    rootPath,
                    basePath,
                    source.TargetPath);

                return stablePath is null || updatePath is null
                    ? null
                    : new DestinationPaths(stablePath, updatePath);
            }

            string? destinationPath = TryGetDirectDestinationPath(
                rootPath,
                basePath,
                source.TargetPath);

            return destinationPath is null
                ? null
                : new DestinationPaths(destinationPath, null);
        }

        private static string? TryGetDirectDestinationPath(
            string rootPath,
            string basePath,
            string relativePath)
        {
            if (!IsSafeRelativePath(relativePath))
            {
                return null;
            }

            try
            {
                string destinationPath = Path.GetFullPath(
                    Path.Combine(
                        basePath,
                        NormalizeSeparators(relativePath)));

                return PathContainment.IsContained(rootPath, destinationPath)
                    ? destinationPath
                    : null;
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
                return null;
            }
        }

        private static bool IsSafeRelativePath(string relativePath)
        {
            try
            {
                if (Path.IsPathRooted(relativePath) ||
                    IsWindowsDriveQualified(relativePath) ||
                    string.IsNullOrWhiteSpace(Path.GetFileName(relativePath)))
                {
                    return false;
                }

                string[] segments = relativePath.Split(
                    new[]
                    {
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar
                    },
                    StringSplitOptions.None);

                if (segments.Any(
                    segment =>
                        segment.Length == 0 ||
                        segment == "." ||
                        segment == ".." ||
                        segment.IndexOfAny(
                            Path.GetInvalidFileNameChars()) >= 0 ||
                        segment.EndsWith(' ') ||
                        segment.EndsWith('.') ||
                        IsWindowsReservedFileName(segment)))
                {
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                PathTooLongException)
            {
                return false;
            }
        }

        private static void ValidateMappingSuffix(
            ResolvedClickOncePublishLayout layout,
            StagingSource source,
            string suffix)
        {
            int separatorIndex = source.TargetPath.LastIndexOfAny(
                new[]
                {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                });
            string targetFileName =
                source.TargetPath[(separatorIndex + 1)..];

            if (string.IsNullOrEmpty(suffix) ||
                source.ManifestReference is null ||
                suffix.IndexOfAny(
                    new[]
                    {
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar,
                        ':'
                    }) >= 0 ||
                !source.File.Name.Equals(
                    $"{targetFileName}{suffix}",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw CreateUnsafePathException(layout, source);
            }
        }

        private static void AddReferenceDestination(
            ResolvedClickOncePublishLayout layout,
            IDictionary<BaseReference, AllocatedStagingPlanEntry> destinations,
            AllocatedStagingPlanEntry entry)
        {
            BaseReference? reference =
                entry.Source.ManifestReference;

            if (reference is null)
            {
                return;
            }

            if (destinations.TryGetValue(
                reference,
                out AllocatedStagingPlanEntry? existing))
            {
                if (!PathsEqual(
                    existing.Destination.FullName,
                    entry.Destination.FullName))
                {
                    throw CreateReferenceDestinationException(
                        layout,
                        existing,
                        entry);
                }

                return;
            }

            destinations.Add(reference, entry);
        }


        private static ClickOnceStagingException
            CreateUnsafePathException(
            ResolvedClickOncePublishLayout layout,
            StagingSource source,
            Exception? innerException = null)
        {
            return new ClickOnceStagingException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceStagingUnsafeTargetPath,
                    source.TargetPath,
                    source.File.FullName),
                layout.Diagnostics,
                innerException);
        }

        private static ClickOnceStagingException
            CreateCollisionException(
            ResolvedClickOncePublishLayout layout,
            AllocatedStagingPlanEntry existing,
            AllocatedStagingPlanEntry entry)
        {
            return new ClickOnceStagingException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceStagingDestinationCollision,
                    existing.Source.File.FullName,
                    entry.Source.File.FullName,
                    existing.Source.TargetPath,
                    entry.Source.TargetPath),
                layout.Diagnostics);
        }

        private static ClickOnceStagingException
            CreateReferenceDestinationException(
            ResolvedClickOncePublishLayout layout,
            AllocatedStagingPlanEntry existing,
            AllocatedStagingPlanEntry entry)
        {
            return new ClickOnceStagingException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceStagingReferenceDestinationConflict,
                    entry.Source.File.FullName,
                    existing.Source.TargetPath,
                    entry.Source.TargetPath),
                layout.Diagnostics);
        }

        private static bool IsFileDirectoryCollision(string left, string right)
        {
            return right.StartsWith(
                    PathContainment.EnsureTrailingSeparator(left),
                    StringComparison.OrdinalIgnoreCase) ||
                left.StartsWith(
                    PathContainment.EnsureTrailingSeparator(right),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWindowsDriveQualified(string path)
        {
            return path.Length >= 2 &&
                path[1] == ':' &&
                char.IsAsciiLetter(path[0]);
        }

        private static bool IsWindowsReservedFileName(string segment)
        {
            int extensionSeparatorIndex = segment.IndexOf('.');
            ReadOnlySpan<char> fileName = extensionSeparatorIndex < 0
                ? segment
                : segment.AsSpan(
                    start: 0,
                    extensionSeparatorIndex);

            if (fileName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (fileName.Length != 4)
            {
                return false;
            }

            ReadOnlySpan<char> prefix = fileName[..3];
            char suffix = fileName[3];

            return (prefix.Equals("COM", StringComparison.OrdinalIgnoreCase) ||
                    prefix.Equals("LPT", StringComparison.OrdinalIgnoreCase)) &&
                (suffix is >= '1' and <= '9' or '\u00b9' or '\u00b2' or
                    '\u00b3');
        }

        private static string NormalizeSeparators(string path)
        {
            return path.Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }

        private sealed class DestinationAllocator
        {
            private readonly ResolvedClickOncePublishLayout _layout;
            private readonly string _rootPath;
            private readonly Dictionary<DestinationClaim, AllocatedStagingPlanEntry>
                _claims = new(DestinationClaimComparer.Instance);

            internal DestinationAllocator(
                ResolvedClickOncePublishLayout layout,
                string rootPath)
            {
                _layout = layout;
                _rootPath = rootPath;
            }

            internal AllocatedStagingPlanEntry Allocate(
                string basePath,
                StagingSource source)
            {
                if (source.MappingAddedSuffix is string suffix)
                {
                    ValidateMappingSuffix(_layout, source, suffix);
                }

                DestinationClaim claim = new(
                    basePath,
                    NormalizeSeparators(source.TargetPath),
                    source.MappingAddedSuffix ?? string.Empty);

                if (_claims.TryGetValue(
                    claim,
                    out AllocatedStagingPlanEntry? claimedEntry))
                {
                    if (!PathsEqual(
                        claimedEntry.Source.File.FullName,
                        source.File.FullName))
                    {
                        throw CreateCollisionException(
                            _layout,
                            claimedEntry,
                            new AllocatedStagingPlanEntry(
                                source,
                                claimedEntry.Destination,
                                claimedEntry.FileInfoUpdateDestination,
                                claim));
                    }

                    return new AllocatedStagingPlanEntry(
                        source,
                        claimedEntry.Destination,
                        claimedEntry.FileInfoUpdateDestination,
                        claim);
                }

                DestinationPaths? directPaths =
                    TryGetDirectDestinationPaths(
                        _rootPath,
                        basePath,
                        source);
                if (directPaths is null)
                {
                    throw CreateUnsafePathException(_layout, source);
                }

                AllocatedStagingPlanEntry entry = CreateEntry(
                    source,
                    directPaths.Value,
                    claim);
                _claims.Add(claim, entry);

                return entry;
            }

            private static AllocatedStagingPlanEntry CreateEntry(
                StagingSource source,
                DestinationPaths paths,
                DestinationClaim claim)
            {
                return new AllocatedStagingPlanEntry(
                    source,
                    new FileInfo(paths.StablePath),
                    paths.UpdatePath is null
                        ? null
                        : new FileInfo(paths.UpdatePath),
                    claim);
            }
        }

        private sealed class DestinationUseComparer :
            IComparer<DestinationUse>
        {
            internal static readonly DestinationUseComparer Instance = new();

            public int Compare(DestinationUse left, DestinationUse right)
            {
                ReadOnlySpan<char> leftPath = left.Path;
                ReadOnlySpan<char> rightPath = right.Path;

                while (true)
                {
                    int leftSeparator = leftPath.IndexOf(
                        Path.DirectorySeparatorChar);
                    int rightSeparator = rightPath.IndexOf(
                        Path.DirectorySeparatorChar);
                    ReadOnlySpan<char> leftSegment = leftSeparator < 0
                        ? leftPath
                        : leftPath[..leftSeparator];
                    ReadOnlySpan<char> rightSegment = rightSeparator < 0
                        ? rightPath
                        : rightPath[..rightSeparator];
                    int comparison = leftSegment.CompareTo(
                        rightSegment,
                        StringComparison.OrdinalIgnoreCase);

                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    if (leftSeparator < 0 || rightSeparator < 0)
                    {
                        return leftSeparator.CompareTo(rightSeparator);
                    }

                    leftPath = leftPath[(leftSeparator + 1)..];
                    rightPath = rightPath[(rightSeparator + 1)..];
                }
            }
        }

        private sealed class DestinationClaimComparer :
            IEqualityComparer<DestinationClaim>
        {
            internal static readonly DestinationClaimComparer Instance = new();

            public bool Equals(
                DestinationClaim left,
                DestinationClaim right)
            {
                return string.Equals(
                        left.BasePath,
                        right.BasePath,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        left.TargetPath,
                        right.TargetPath,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        left.MappingAddedSuffix,
                        right.MappingAddedSuffix,
                        StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(DestinationClaim value)
            {
                return HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(
                        value.BasePath),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(
                        value.TargetPath),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(
                        value.MappingAddedSuffix));
            }
        }

        private sealed class StagingSource
        {
            internal StagingSource(
                FileInfo file,
                string targetPath,
                BaseReference? manifestReference,
                string? mappingAddedSuffix)
            {
                ArgumentNullException.ThrowIfNull(file, nameof(file));
                ArgumentException.ThrowIfNullOrWhiteSpace(
                    targetPath,
                    nameof(targetPath));

                File = file;
                TargetPath = targetPath;
                ManifestReference = manifestReference;
                MappingAddedSuffix = mappingAddedSuffix;
            }

            internal FileInfo File { get; }
            internal string TargetPath { get; }
            internal BaseReference? ManifestReference { get; }
            internal string? MappingAddedSuffix { get; }
        }

        private sealed class AllocatedStagingPlanEntry
        {
            internal AllocatedStagingPlanEntry(
                StagingSource source,
                FileInfo destination,
                FileInfo? fileInfoUpdateDestination,
                DestinationClaim claim)
            {
                Source = source;
                Destination = destination;
                FileInfoUpdateDestination = fileInfoUpdateDestination;
                Claim = claim;
                Entry = new ClickOnceStagingPlanEntry(
                    source.File,
                    destination,
                    fileInfoUpdateDestination,
                    source.TargetPath,
                    source.ManifestReference);
            }

            internal StagingSource Source { get; }
            internal FileInfo Destination { get; }
            internal FileInfo? FileInfoUpdateDestination { get; }
            internal DestinationClaim Claim { get; }
            internal ClickOnceStagingPlanEntry Entry { get; }
        }

        private readonly record struct DestinationUse(
            string Path,
            AllocatedStagingPlanEntry Entry,
            bool IsFileInfoUpdateDestination);

        private readonly record struct DestinationPaths(
            string StablePath,
            string? UpdatePath);

        private readonly record struct DestinationClaim(
            string BasePath,
            string TargetPath,
            string MappingAddedSuffix);
    }
}
