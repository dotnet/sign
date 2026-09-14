// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ClickOncePublishLayoutStager
    {
        private readonly IDirectoryService _directoryService;
        private readonly ClickOnceStagingPlanBuilder _planBuilder;

        internal ClickOncePublishLayoutStager(
            IDirectoryService directoryService)
            : this(directoryService, new ClickOnceStagingPlanBuilder())
        {
        }

        internal ClickOncePublishLayoutStager(
            IDirectoryService directoryService,
            ClickOnceStagingPlanBuilder planBuilder)
        {
            ArgumentNullException.ThrowIfNull(
                directoryService,
                nameof(directoryService));
            ArgumentNullException.ThrowIfNull(
                planBuilder,
                nameof(planBuilder));

            _directoryService = directoryService;
            _planBuilder = planBuilder;
        }

        internal ClickOnceStagingSession Stage(
            ResolvedClickOncePublishLayout layout)
        {
            ArgumentNullException.ThrowIfNull(layout, nameof(layout));

            TemporaryDirectory temporaryDirectory =
                new(_directoryService);

            try
            {
                ClickOnceStagingPlan plan = _planBuilder.Build(
                    layout,
                    temporaryDirectory.Directory);

                CopyFiles(layout, plan);

                Dictionary<BaseReference, string?> originalResolvedPaths =
                    BindManifestReferences(plan);
                Dictionary<
                    ClickOnceStagingPlanEntry,
                    ClickOnceStagedFile> stagedFiles =
                        plan.Entries.ToDictionary(
                            entry => entry,
                            CreateStagedFile);

                return new ClickOnceStagingSession(
                    layout.Diagnostics,
                    temporaryDirectory,
                    plan.DeploymentManifest is null
                        ? null
                        : stagedFiles[plan.DeploymentManifest],
                    stagedFiles[plan.ApplicationManifest],
                    plan.Payloads.ToDictionary(
                        pair => pair.Key,
                        pair => stagedFiles[pair.Value]),
                    plan.AdjacentExecutables.ToDictionary(
                        pair => pair.Key,
                        pair => stagedFiles[pair.Value]),
                    originalResolvedPaths);
            }
            catch
            {
                temporaryDirectory.Dispose();

                throw;
            }
        }

        private static void CopyFiles(
            ResolvedClickOncePublishLayout layout,
            ClickOnceStagingPlan plan)
        {
            HashSet<string> copiedDestinations =
                new(StringComparer.OrdinalIgnoreCase);
            ClickOnceStagingPlanEntry? currentEntry = null;

            try
            {
                foreach (ClickOnceStagingPlanEntry entry in plan.Entries)
                {
                    currentEntry = entry;

                    if (!copiedDestinations.Add(
                        entry.Destination.FullName))
                    {
                        continue;
                    }

                    entry.Destination.Directory!.Create();
                    entry.Source.CopyTo(
                        entry.Destination.FullName,
                        overwrite: false);
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                throw new ClickOnceStagingException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceStagingCopyFailed,
                        currentEntry?.Source.FullName ?? string.Empty),
                    layout.Diagnostics,
                    exception);
            }
        }

        private static Dictionary<BaseReference, string?>
            BindManifestReferences(ClickOnceStagingPlan plan)
        {
            Dictionary<BaseReference, string?> originalResolvedPaths =
                new(ReferenceEqualityComparer.Instance);

            foreach (ClickOnceStagingPlanEntry entry in plan.Entries)
            {
                BaseReference? reference = entry.ManifestReference;

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

        private static ClickOnceStagedFile CreateStagedFile(
            ClickOnceStagingPlanEntry entry)
        {
            return new ClickOnceStagedFile(
                entry.Source,
                entry.Destination,
                entry.FileInfoUpdateDestination,
                entry.TargetPath,
                entry.ManifestReference);
        }
    }
}
