// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    // The files a group of signing operations can request. Every plan
    // includes its physical input and entries of containers within it. A
    // signer that requires sequential coordination (ClickOnce) also includes
    // files in the input's directory tree and entries of containers within
    // them.
    internal sealed class SigningOperationScope
    {
        private readonly IReadOnlySet<SigningSourceIdentity> _files;
        private readonly IReadOnlyList<SigningSourceIdentity>
            _directoryTrees;

        private SigningOperationScope(
            IReadOnlySet<SigningSourceIdentity> files,
            IReadOnlyList<SigningSourceIdentity> directoryTrees)
        {
            _files = files;
            _directoryTrees = directoryTrees;
        }

        internal static SigningOperationScope Create(
            SigningOperationPlan plan,
            Func<FileInfo, bool> requiresSequentialCoordination)
        {
            ArgumentNullException.ThrowIfNull(plan, nameof(plan));

            return Create(
                new[] { plan },
                requiresSequentialCoordination);
        }

        internal static SigningOperationScope Create(
            IReadOnlyList<SigningOperationPlan> plans,
            Func<FileInfo, bool> requiresSequentialCoordination)
        {
            ArgumentNullException.ThrowIfNull(plans, nameof(plans));
            ArgumentNullException.ThrowIfNull(
                requiresSequentialCoordination,
                nameof(requiresSequentialCoordination));

            HashSet<SigningSourceIdentity> files = new();
            List<SigningSourceIdentity> directoryTrees = new();

            foreach (SigningOperationPlan plan in plans)
            {
                SigningSourceIdentity identity =
                    plan.Source.SourceIdentity;

                if (!ReferenceEquals(identity.GetPhysicalRoot(), identity))
                {
                    throw new ArgumentException(
                        message:
                            "Every plan's source must be a physical file.",
                        paramName: nameof(plans));
                }

                if (requiresSequentialCoordination(plan.Source.File))
                {
                    if (!directoryTrees.Contains(identity))
                    {
                        directoryTrees.Add(identity);
                    }
                }
                else
                {
                    files.Add(identity);
                }
            }

            return new SigningOperationScope(
                files,
                directoryTrees);
        }

        internal bool Contains(SigningSourceIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity, nameof(identity));

            SigningSourceIdentity physicalRoot =
                identity.GetPhysicalRoot();

            return _files.Contains(physicalRoot) ||
                _directoryTrees.Any(identity.IsInDirectoryTreeOf);
        }

        // Returns connected components of plans whose scopes overlap. No
        // signing result can be reused across components.
        internal static IReadOnlyList<SigningOperationGroup> CreateGroups(
                IReadOnlyList<SigningOperationPlan> plans,
                Func<FileInfo, bool> requiresSequentialCoordination)
        {
            return CreateGroups(
                plans,
                requiresSequentialCoordination,
                out _);
        }

        internal static IReadOnlyList<SigningOperationGroup> CreateGroups(
                IReadOnlyList<SigningOperationPlan> plans,
                Func<FileInfo, bool> requiresSequentialCoordination,
                out int directoryOperationCount)
        {
            ArgumentNullException.ThrowIfNull(plans, nameof(plans));
            ArgumentNullException.ThrowIfNull(
                requiresSequentialCoordination,
                nameof(requiresSequentialCoordination));

            int operationCount = 0;

            List<(
                SigningOperationPlan Plan,
                SigningOperationScope Scope,
                SigningSourceIdentity Identity)> candidates = new();

            foreach (SigningOperationPlan plan in plans)
            {
                candidates.Add((
                    plan,
                    Create(plan, requiresSequentialCoordination),
                    plan.Source.SourceIdentity));
            }

            int[] parents = Enumerable.Range(
                    start: 0,
                    count: candidates.Count)
                .ToArray();

            int Find(int index)
            {
                while (parents[index] != index)
                {
                    parents[index] = parents[parents[index]];
                    index = parents[index];
                }

                return index;
            }

            void Union(int left, int right)
            {
                int leftRoot = Find(left);
                int rightRoot = Find(right);

                if (leftRoot != rightRoot)
                {
                    parents[rightRoot] = leftRoot;
                }
            }

            Dictionary<SigningSourceIdentity, int> physicalSources = new();

            for (int index = 0; index < candidates.Count; ++index)
            {
                SigningSourceIdentity identity =
                    candidates[index].Identity;

                if (physicalSources.TryGetValue(
                    identity,
                    out int existingIndex))
                {
                    Union(existingIndex, index);
                }
                else
                {
                    physicalSources.Add(identity, index);
                }
            }

            List<(string Path, int CandidateIndex)>?
                sortedPhysicalSources = null;
            Dictionary<string, (int Start, int End)> directoryRangesByPath =
                new(StringComparer.OrdinalIgnoreCase);
            List<(int Start, int End, int CandidateIndex)> directoryRanges =
                new();

            for (int index = 0; index < candidates.Count; ++index)
            {
                SigningOperationScope scope = candidates[index].Scope;

                if (scope._directoryTrees.Count == 0)
                {
                    continue;
                }

                sortedPhysicalSources ??= physicalSources
                    .Select(
                        source => (
                            source.Key.GetPhysicalPath(),
                            source.Value))
                    .OrderBy(
                        source => source.Item1,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string directory = PathContainment.EnsureTrailingSeparator(
                    Path.GetDirectoryName(
                        scope._directoryTrees[0].GetPhysicalPath())!);

                if (!directoryRangesByPath.TryGetValue(
                    directory,
                    out (int Start, int End) range))
                {
                    int start = FindFirstPathAtOrAfter(
                        sortedPhysicalSources,
                        directory,
                        ref operationCount);
                    int end = FindFirstPathOutsideDirectory(
                        sortedPhysicalSources,
                        directory,
                        start,
                        ref operationCount);

                    range = (start, end);
                    directoryRangesByPath.Add(directory, range);
                }

                directoryRanges.Add((
                    range.Start,
                    range.End,
                    index));
            }

            if (directoryRanges.Count > 0)
            {
                directoryRanges.Sort(
                    (left, right) =>
                    {
                        int comparison = left.Start.CompareTo(right.Start);

                        return comparison != 0
                            ? comparison
                            : left.End.CompareTo(right.End);
                    });

                (int Start, int End, int CandidateIndex) current =
                    directoryRanges[0];

                for (
                    int rangeIndex = 1;
                    rangeIndex < directoryRanges.Count;
                    ++rangeIndex)
                {
                    (int Start, int End, int CandidateIndex) next =
                        directoryRanges[rangeIndex];

                    if (next.Start < current.End)
                    {
                        current.End = Math.Max(current.End, next.End);
                    }
                    else
                    {
                        UnionRange(current);
                        current = next;
                    }
                }

                UnionRange(current);
            }

            void UnionRange(
                (int Start, int End, int CandidateIndex) range)
            {
                for (
                    int sourceIndex = range.Start;
                    sourceIndex < range.End;
                    ++sourceIndex)
                {
                    ++operationCount;
                    Union(
                        range.CandidateIndex,
                        sortedPhysicalSources![sourceIndex].CandidateIndex);
                }
            }

            Dictionary<
                int,
                List<(SigningOperationPlan Plan, SigningOperationScope Scope)>>
                    groups = new();

            for (int index = 0; index < candidates.Count; ++index)
            {
                int root = Find(index);

                if (!groups.TryGetValue(
                    root,
                    out List<(
                        SigningOperationPlan Plan,
                        SigningOperationScope Scope)>? group))
                {
                    group = new List<(
                        SigningOperationPlan Plan,
                        SigningOperationScope Scope)>();
                    groups.Add(root, group);
                }

                group.Add((
                    candidates[index].Plan,
                    candidates[index].Scope));
            }

            directoryOperationCount = operationCount;

            return groups.Values
                .Select(
                    group => new SigningOperationGroup(
                        group.Select(candidate => candidate.Plan).ToList(),
                        Combine(
                            group.Select(
                                candidate => candidate.Scope))))
                .ToList();
        }

        private static int FindFirstPathAtOrAfter(
            IReadOnlyList<(string Path, int CandidateIndex)> sortedSources,
            string path,
            ref int operationCount)
        {
            int lower = 0;
            int upper = sortedSources.Count;

            while (lower < upper)
            {
                ++operationCount;
                int middle = lower + ((upper - lower) / 2);

                if (StringComparer.OrdinalIgnoreCase.Compare(
                    sortedSources[middle].Path,
                    path) < 0)
                {
                    lower = middle + 1;
                }
                else
                {
                    upper = middle;
                }
            }

            return lower;
        }

        private static int FindFirstPathOutsideDirectory(
            IReadOnlyList<(string Path, int CandidateIndex)> sortedSources,
            string directory,
            int start,
            ref int operationCount)
        {
            int lower = start;
            int upper = sortedSources.Count;

            while (lower < upper)
            {
                ++operationCount;
                int middle = lower + ((upper - lower) / 2);

                if (PathContainment.IsContained(
                    directory,
                    sortedSources[middle].Path))
                {
                    lower = middle + 1;
                }
                else
                {
                    upper = middle;
                }
            }

            return lower;
        }

        private static SigningOperationScope Combine(
            IEnumerable<SigningOperationScope> scopes)
        {
            HashSet<SigningSourceIdentity> files = new();
            List<SigningSourceIdentity> directoryTrees = new();

            foreach (SigningOperationScope scope in scopes)
            {
                files.UnionWith(scope._files);

                foreach (
                    SigningSourceIdentity directoryTree
                    in scope._directoryTrees)
                {
                    if (!directoryTrees.Contains(directoryTree))
                    {
                        directoryTrees.Add(directoryTree);
                    }
                }
            }

            return new SigningOperationScope(files, directoryTrees);
        }
    }
}
