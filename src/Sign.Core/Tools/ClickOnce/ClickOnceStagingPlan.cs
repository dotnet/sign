// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class ClickOnceStagingPlan
    {
        internal ClickOnceStagingPlan(
            ClickOnceStagingPlanEntry? deploymentManifest,
            ClickOnceStagingPlanEntry applicationManifest,
            IReadOnlyDictionary<
                ResolvedClickOncePayload,
                ClickOnceStagingPlanEntry> payloads,
            IReadOnlyDictionary<
                ResolvedClickOnceAdjacentExecutable,
                ClickOnceStagingPlanEntry> adjacentExecutables)
        {
            ArgumentNullException.ThrowIfNull(
                applicationManifest,
                nameof(applicationManifest));
            ArgumentNullException.ThrowIfNull(payloads, nameof(payloads));
            ArgumentNullException.ThrowIfNull(
                adjacentExecutables,
                nameof(adjacentExecutables));

            DeploymentManifest = deploymentManifest;
            ApplicationManifest = applicationManifest;
            Payloads = new Dictionary<
                ResolvedClickOncePayload,
                ClickOnceStagingPlanEntry>(payloads);
            AdjacentExecutables = new Dictionary<
                ResolvedClickOnceAdjacentExecutable,
                ClickOnceStagingPlanEntry>(adjacentExecutables);
            Entries = GetEntries().ToArray();
        }

        internal ClickOnceStagingPlanEntry? DeploymentManifest { get; }
        internal ClickOnceStagingPlanEntry ApplicationManifest { get; }
        internal IReadOnlyDictionary<
            ResolvedClickOncePayload,
            ClickOnceStagingPlanEntry> Payloads { get; }
        internal IReadOnlyDictionary<
            ResolvedClickOnceAdjacentExecutable,
            ClickOnceStagingPlanEntry> AdjacentExecutables { get; }
        internal IReadOnlyList<ClickOnceStagingPlanEntry> Entries { get; }

        private IEnumerable<ClickOnceStagingPlanEntry> GetEntries()
        {
            if (DeploymentManifest is not null)
            {
                yield return DeploymentManifest;
            }

            yield return ApplicationManifest;

            foreach (ClickOnceStagingPlanEntry payload in Payloads.Values)
            {
                yield return payload;
            }

            foreach (
                ClickOnceStagingPlanEntry adjacentExecutable
                in AdjacentExecutables.Values)
            {
                yield return adjacentExecutable;
            }
        }
    }
}
