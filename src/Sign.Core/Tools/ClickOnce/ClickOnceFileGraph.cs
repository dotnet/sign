// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class ClickOnceFileGraph
    {
        internal ClickOnceFileGraph(
            ClickOnceFileGraphEntry? deploymentManifest,
            IDeployManifest? deployManifest,
            ClickOnceFileGraphEntry applicationManifest,
            IApplicationManifest applicationManifestModel,
            IEnumerable<ClickOnceFileGraphEntry> payloads,
            IEnumerable<ClickOnceFileGraphEntry> adjacentExecutables,
            IEnumerable<ClickOnceManifestDiagnostic> diagnostics)
        {
            ArgumentNullException.ThrowIfNull(applicationManifest, nameof(applicationManifest));
            ArgumentNullException.ThrowIfNull(applicationManifestModel, nameof(applicationManifestModel));
            ArgumentNullException.ThrowIfNull(payloads, nameof(payloads));
            ArgumentNullException.ThrowIfNull(adjacentExecutables, nameof(adjacentExecutables));
            ArgumentNullException.ThrowIfNull(diagnostics, nameof(diagnostics));

            DeploymentManifest = deploymentManifest;
            DeployManifest = deployManifest;
            ApplicationManifest = applicationManifest;
            ApplicationManifestModel = applicationManifestModel;
            Payloads = payloads.ToArray();
            AdjacentExecutables = adjacentExecutables.ToArray();
            Diagnostics = diagnostics.ToArray();
        }

        internal ClickOnceFileGraphEntry? DeploymentManifest { get; }
        internal IDeployManifest? DeployManifest { get; }
        internal ClickOnceFileGraphEntry ApplicationManifest { get; }
        internal IApplicationManifest ApplicationManifestModel { get; }
        internal IReadOnlyList<ClickOnceFileGraphEntry> Payloads { get; }
        internal IReadOnlyList<ClickOnceFileGraphEntry> AdjacentExecutables { get; }
        internal IReadOnlyList<ClickOnceManifestDiagnostic> Diagnostics { get; }
    }
}
