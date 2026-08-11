// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ClickOnceFileGraphStager
    {
        private readonly IDirectoryService _directoryService;

        internal ClickOnceFileGraphStager(IDirectoryService directoryService)
        {
            ArgumentNullException.ThrowIfNull(
                directoryService,
                nameof(directoryService));

            _directoryService = directoryService;
        }

        internal ClickOnceStagingResult Stage(
            ClickOnceFileGraph graph,
            ClickOnceSigningMode mode)
        {
            ArgumentNullException.ThrowIfNull(graph, nameof(graph));

            if (!Enum.IsDefined(mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            TemporaryDirectory temporaryDirectory = new(_directoryService);

            try
            {
                IReadOnlyList<StagingCandidate> candidates =
                    GetCandidates(graph, mode);
                IReadOnlyList<StagingPlanEntry> plan = CreatePlan(
                    graph,
                    temporaryDirectory.Directory,
                    candidates);

                CopyFiles(graph, plan);

                Dictionary<BaseReference, string?> originalResolvedPaths =
                    BindManifestReferences(plan);
                ClickOnceStagedFile[] files = plan
                    .Select(
                        entry => new ClickOnceStagedFile(
                            entry.Candidate.GraphEntry,
                            entry.Destination,
                            entry.UpdateDestination,
                            entry.Candidate.IsUpdateInputOnly))
                    .ToArray();

                return new ClickOnceStagingResult(
                    graph,
                    mode,
                    temporaryDirectory,
                    files,
                    originalResolvedPaths);
            }
            catch
            {
                temporaryDirectory.Dispose();

                throw;
            }
        }

        private static IReadOnlyList<StagingCandidate> GetCandidates(
            ClickOnceFileGraph graph,
            ClickOnceSigningMode mode)
        {
            List<StagingCandidate> candidates = new();
            bool isDeploymentInput = graph.DeploymentManifest is not null;

            if (isDeploymentInput)
            {
                candidates.Add(
                    new StagingCandidate(
                        graph.DeploymentManifest!,
                        isUpdateInputOnly: false));
            }
            else
            {
                candidates.Add(
                    new StagingCandidate(
                        graph.ApplicationManifest,
                        isUpdateInputOnly: false));
            }

            if (mode == ClickOnceSigningMode.NoUpdate)
            {
                return candidates;
            }

            if (isDeploymentInput)
            {
                candidates.Add(
                    new StagingCandidate(
                        graph.ApplicationManifest,
                        isUpdateInputOnly:
                            mode ==
                            ClickOnceSigningMode.NoSignDependencies));

                if (mode == ClickOnceSigningMode.NoSignDependencies)
                {
                    return candidates;
                }
            }

            bool dependenciesAreUpdateInputs =
                mode == ClickOnceSigningMode.NoSignDependencies;

            candidates.AddRange(
                graph.Payloads.Select(
                    entry => new StagingCandidate(
                        entry,
                        dependenciesAreUpdateInputs)));

            if (mode == ClickOnceSigningMode.Default)
            {
                candidates.AddRange(
                    graph.AdjacentExecutables.Select(
                        entry => new StagingCandidate(
                            entry,
                            isUpdateInputOnly: false)));
            }

            return candidates;
        }

        private static IReadOnlyList<StagingPlanEntry> CreatePlan(
            ClickOnceFileGraph graph,
            DirectoryInfo stagingDirectory,
            IReadOnlyList<StagingCandidate> candidates)
        {
            string rootPath = EnsureTrailingSeparator(
                Path.GetFullPath(stagingDirectory.FullName));
            DestinationAllocator allocator = new(graph, rootPath);
            StagingCandidate? applicationManifestCandidate =
                candidates.FirstOrDefault(
                    candidate =>
                        candidate.GraphEntry.Kind ==
                        ClickOnceFileGraphEntryKind.ApplicationManifest);
            List<StagingPlanEntry> plan = new(candidates.Count);
            Dictionary<BaseReference, StagingPlanEntry>
                referenceDestinations =
                    new(ReferenceEqualityComparer.Instance);
            string applicationDirectoryPath = rootPath;

            for (int index = 0; index < candidates.Count; ++index)
            {
                StagingCandidate candidate = candidates[index];
                StagingPlanEntry entry = allocator.Allocate(
                    applicationDirectoryPath,
                    candidate,
                    index);
                AddReferenceDestination(
                    graph,
                    referenceDestinations,
                    entry);

                plan.Add(entry);

                if (ReferenceEquals(candidate, applicationManifestCandidate))
                {
                    applicationDirectoryPath =
                        entry.Destination.Directory!.FullName;
                }
            }

            ValidateDestinations(graph, plan);

            return plan;
        }

        private static void ValidateDestinations(
            ClickOnceFileGraph graph,
            IReadOnlyList<StagingPlanEntry> plan)
        {
            List<DestinationUse> destinations = new(plan.Count * 2); // Stable and optional update paths.

            foreach (StagingPlanEntry entry in plan)
            {
                destinations.Add(
                    new DestinationUse(
                        entry.Destination.FullName,
                        entry,
                        false));

                if (entry.UpdateDestination is not null)
                {
                    destinations.Add(
                        new DestinationUse(
                            entry.UpdateDestination.FullName,
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
                            graph,
                            previous.Entry,
                            current.Entry);
                    }
                }
                else if (IsFileDirectoryCollision(
                    previous.Path,
                    current.Path))
                {
                    throw CreateCollisionException(
                        graph,
                        previous.Entry,
                        current.Entry);
                }
            }
        }

        private static bool AreReusableDuplicates(
            DestinationUse left,
            DestinationUse right)
        {
            return left.IsUpdateDestination == right.IsUpdateDestination &&
                DestinationClaimComparer.Instance.Equals(
                    left.Entry.Claim,
                    right.Entry.Claim) &&
                PathsEqual(
                    left.Entry.Candidate.GraphEntry.Source.FullName,
                    right.Entry.Candidate.GraphEntry.Source.FullName);
        }

        private static DestinationPaths? TryGetDirectDestinationPaths(
            string rootPath,
            string basePath,
            ClickOnceFileGraphEntry entry)
        {
            if (entry.MappingAddedSuffix is string suffix)
            {
                string? stablePath = TryGetDirectDestinationPath(
                    rootPath,
                    basePath,
                    entry.TargetPath + suffix);
                string? updatePath = TryGetDirectDestinationPath(
                    rootPath,
                    basePath,
                    entry.TargetPath);

                return stablePath is null || updatePath is null
                    ? null
                    : new DestinationPaths(stablePath, updatePath);
            }

            string? destinationPath = TryGetDirectDestinationPath(
                rootPath,
                basePath,
                entry.TargetPath);

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

                return IsContained(rootPath, destinationPath)
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
                        segment.EndsWith('.')))
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
            ClickOnceFileGraph graph,
            ClickOnceFileGraphEntry entry,
            string suffix)
        {
            int separatorIndex = entry.TargetPath.LastIndexOfAny(
                new[]
                {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                });
            string targetFileName = entry.TargetPath[(separatorIndex + 1)..];

            if (string.IsNullOrEmpty(suffix) ||
                entry.ManifestReference is null ||
                suffix.IndexOfAny(
                    new[]
                    {
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar,
                        ':'
                    }) >= 0 ||
                !entry.Source.Name.Equals(
                    $"{targetFileName}{suffix}",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw CreateUnsafePathException(graph, entry);
            }
        }

        private static void AddReferenceDestination(
            ClickOnceFileGraph graph,
            IDictionary<BaseReference, StagingPlanEntry> destinations,
            StagingPlanEntry entry)
        {
            BaseReference? reference =
                entry.Candidate.GraphEntry.ManifestReference;

            if (reference is null)
            {
                return;
            }

            if (destinations.TryGetValue(
                reference,
                out StagingPlanEntry? existing))
            {
                if (!PathsEqual(
                    existing.Destination.FullName,
                    entry.Destination.FullName))
                {
                    throw CreateReferenceDestinationException(
                        graph,
                        existing,
                        entry);
                }

                return;
            }

            destinations.Add(reference, entry);
        }


        private static void CopyFiles(
            ClickOnceFileGraph graph,
            IReadOnlyList<StagingPlanEntry> plan)
        {
            HashSet<string> copiedDestinations =
                new(StringComparer.OrdinalIgnoreCase);
            StagingPlanEntry? currentEntry = null;

            try
            {
                foreach (StagingPlanEntry entry in plan)
                {
                    currentEntry = entry;

                    if (!copiedDestinations.Add(entry.Destination.FullName))
                    {
                        continue;
                    }

                    entry.Destination.Directory!.Create();
                    entry.Candidate.GraphEntry.Source.CopyTo(
                        entry.Destination.FullName,
                        overwrite: false);
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                throw new ClickOnceFileGraphStagingException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceStagingCopyFailed,
                        currentEntry?.Candidate.GraphEntry.Source.FullName
                            ?? string.Empty),
                    graph.Diagnostics,
                    exception);
            }
        }

        private static Dictionary<BaseReference, string?>
            BindManifestReferences(IReadOnlyList<StagingPlanEntry> plan)
        {
            Dictionary<BaseReference, string?> originalResolvedPaths =
                new(ReferenceEqualityComparer.Instance);

            foreach (StagingPlanEntry entry in plan)
            {
                BaseReference? reference =
                    entry.Candidate.GraphEntry.ManifestReference;

                if (reference is null)
                {
                    continue;
                }

                originalResolvedPaths.TryAdd(
                    reference,
                    reference.ResolvedPath);
                reference.ResolvedPath = entry.Destination.FullName;
            }

            return originalResolvedPaths;
        }

        private static ClickOnceFileGraphStagingException
            CreateUnsafePathException(
            ClickOnceFileGraph graph,
            ClickOnceFileGraphEntry entry,
            Exception? innerException = null)
        {
            return new ClickOnceFileGraphStagingException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceStagingUnsafeTargetPath,
                    entry.TargetPath,
                    entry.Source.FullName),
                graph.Diagnostics,
                innerException);
        }

        private static ClickOnceFileGraphStagingException
            CreateCollisionException(
            ClickOnceFileGraph graph,
            StagingPlanEntry existing,
            StagingPlanEntry entry)
        {
            return new ClickOnceFileGraphStagingException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceStagingDestinationCollision,
                    existing.Candidate.GraphEntry.Source.FullName,
                    entry.Candidate.GraphEntry.Source.FullName,
                    existing.Candidate.GraphEntry.TargetPath,
                    entry.Candidate.GraphEntry.TargetPath),
                graph.Diagnostics);
        }

        private static ClickOnceFileGraphStagingException
            CreateReferenceDestinationException(
            ClickOnceFileGraph graph,
            StagingPlanEntry existing,
            StagingPlanEntry entry)
        {
            return new ClickOnceFileGraphStagingException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceStagingReferenceDestinationConflict,
                    entry.Candidate.GraphEntry.Source.FullName,
                    existing.Candidate.GraphEntry.TargetPath,
                    entry.Candidate.GraphEntry.TargetPath),
                graph.Diagnostics);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return Path.EndsInDirectorySeparator(path)
                ? path
                : $"{path}{Path.DirectorySeparatorChar}";
        }

        private static bool IsContained(string rootPath, string path)
        {
            return path.StartsWith(
                rootPath,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFileDirectoryCollision(string left, string right)
        {
            return right.StartsWith(
                    EnsureTrailingSeparator(left),
                    StringComparison.OrdinalIgnoreCase) ||
                left.StartsWith(
                    EnsureTrailingSeparator(right),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWindowsDriveQualified(string path)
        {
            return path.Length >= 2 &&
                path[1] == ':' &&
                char.IsAsciiLetter(path[0]);
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
            private const string InternalDirectoryPrefix =
                "__clickonce_internal_";
            private const string RemappedFileName = "file";
            private readonly ClickOnceFileGraph _graph;
            private readonly string _rootPath;
            private readonly Dictionary<DestinationClaim, StagingPlanEntry>
                _claims = new(DestinationClaimComparer.Instance);

            internal DestinationAllocator(
                ClickOnceFileGraph graph,
                string rootPath)
            {
                _graph = graph;
                _rootPath = rootPath;
            }

            internal StagingPlanEntry Allocate(
                string applicationDirectoryPath,
                StagingCandidate candidate,
                int candidateIndex)
            {
                ClickOnceFileGraphEntry graphEntry = candidate.GraphEntry;

                if (graphEntry.MappingAddedSuffix is string suffix)
                {
                    ValidateMappingSuffix(_graph, graphEntry, suffix);
                }

                string basePath =
                    graphEntry.Kind == ClickOnceFileGraphEntryKind.Payload
                        ? applicationDirectoryPath
                        : _rootPath;
                DestinationClaim claim = new(
                    basePath,
                    NormalizeSeparators(graphEntry.TargetPath),
                    graphEntry.MappingAddedSuffix ?? string.Empty);

                if (_claims.TryGetValue(
                    claim,
                    out StagingPlanEntry? claimedEntry))
                {
                    if (!PathsEqual(
                        claimedEntry.Candidate.GraphEntry.Source.FullName,
                        graphEntry.Source.FullName))
                    {
                        throw CreateCollisionException(
                            _graph,
                            claimedEntry,
                            new StagingPlanEntry(
                                candidate,
                                claimedEntry.Destination,
                                claimedEntry.UpdateDestination,
                                claimedEntry.IsRemapped,
                                claim));
                    }

                    return new StagingPlanEntry(
                        candidate,
                        claimedEntry.Destination,
                        claimedEntry.UpdateDestination,
                        claimedEntry.IsRemapped,
                        claim);
                }

                DestinationPaths? directPaths =
                    TryGetDirectDestinationPaths(
                        _rootPath,
                        basePath,
                        graphEntry);
                StagingPlanEntry entry;

                if (directPaths is not null &&
                    !UsesReservedInternalNamespace(basePath, directPaths.Value))
                {
                    entry = CreateEntry(
                        candidate,
                        directPaths.Value,
                        isRemapped: false,
                        claim);
                }
                else
                {
                    entry = AllocateRemapped(
                        candidate,
                        candidateIndex,
                        directPaths,
                        claim);
                }

                _claims.Add(claim, entry);

                return entry;
            }

            private StagingPlanEntry AllocateRemapped(
                StagingCandidate candidate,
                int candidateIndex,
                DestinationPaths? directPaths,
                DestinationClaim claim)
            {
                string? mappingAddedSuffix =
                    candidate.GraphEntry.MappingAddedSuffix;
                string directoryPath = Path.Combine(
                    _rootPath,
                    GetGeneratedNamespace(directPaths),
                    $"{candidateIndex:x8}");
                DestinationPaths paths = new(
                    Path.Combine(
                        directoryPath,
                        $"{RemappedFileName}{mappingAddedSuffix}"),
                    mappingAddedSuffix is not null
                        ? Path.Combine(directoryPath, RemappedFileName)
                        : null);

                return CreateEntry(
                    candidate,
                    paths,
                    isRemapped: true,
                    claim);
            }

            private string GetGeneratedNamespace(
                DestinationPaths? directPaths)
            {
                string first = $"{InternalDirectoryPrefix}generated0";
                string second = $"{InternalDirectoryPrefix}generated1";

                if (directPaths is null)
                {
                    return first;
                }

                HashSet<string> directFirstSegments = new(
                    GetPaths(directPaths.Value).Select(
                        path =>
                        {
                            string relativePath = Path.GetRelativePath(
                                _rootPath,
                                path);
                            int separatorIndex = relativePath.IndexOf(
                                Path.DirectorySeparatorChar);

                            return separatorIndex < 0
                                ? relativePath
                                : relativePath[..separatorIndex];
                        }),
                    StringComparer.OrdinalIgnoreCase);

                if (!directFirstSegments.Contains(first))
                {
                    return first;
                }

                return !directFirstSegments.Contains(second)
                    ? second
                    : $"{InternalDirectoryPrefix}generated2";
            }

            private bool UsesReservedInternalNamespace(
                string basePath,
                DestinationPaths paths)
            {
                if (!PathsEqual(_rootPath, basePath))
                {
                    return false;
                }

                return GetPaths(paths).Any(
                    path =>
                    {
                        string relativePath = Path.GetRelativePath(
                            _rootPath,
                            path);
                        int separatorIndex = relativePath.IndexOf(
                            Path.DirectorySeparatorChar);
                        string firstSegment = separatorIndex < 0
                            ? relativePath
                            : relativePath[..separatorIndex];

                        return firstSegment.StartsWith(
                            InternalDirectoryPrefix,
                            StringComparison.OrdinalIgnoreCase);
                    });
            }

            private static string[] GetPaths(DestinationPaths paths)
            {
                return paths.UpdatePath is null
                    ? new[] { paths.StablePath }
                    : new[] { paths.StablePath, paths.UpdatePath };
            }

            private static StagingPlanEntry CreateEntry(
                StagingCandidate candidate,
                DestinationPaths paths,
                bool isRemapped,
                DestinationClaim claim)
            {
                return new StagingPlanEntry(
                    candidate,
                    new FileInfo(paths.StablePath),
                    paths.UpdatePath is null
                        ? null
                        : new FileInfo(paths.UpdatePath),
                    isRemapped,
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

        private sealed class StagingCandidate
        {
            internal StagingCandidate(
                ClickOnceFileGraphEntry graphEntry,
                bool isUpdateInputOnly)
            {
                GraphEntry = graphEntry;
                IsUpdateInputOnly = isUpdateInputOnly;
            }

            internal ClickOnceFileGraphEntry GraphEntry { get; }
            internal bool IsUpdateInputOnly { get; }
        }

        private sealed class StagingPlanEntry
        {
            internal StagingPlanEntry(
                StagingCandidate candidate,
                FileInfo destination,
                FileInfo? updateDestination,
                bool isRemapped,
                DestinationClaim claim)
            {
                Candidate = candidate;
                Destination = destination;
                UpdateDestination = updateDestination;
                IsRemapped = isRemapped;
                Claim = claim;
            }

            internal StagingCandidate Candidate { get; }
            internal FileInfo Destination { get; }
            internal FileInfo? UpdateDestination { get; }
            internal bool IsRemapped { get; }
            internal DestinationClaim Claim { get; }
        }

        private readonly record struct DestinationUse(
            string Path,
            StagingPlanEntry Entry,
            bool IsUpdateDestination);

        private readonly record struct DestinationPaths(
            string StablePath,
            string? UpdatePath);

        private readonly record struct DestinationClaim(
            string BasePath,
            string TargetPath,
            string MappingAddedSuffix);
    }
}
