// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class ResolvedClickOncePublishLayout
    {
        internal ResolvedClickOncePublishLayout(
            ResolvedClickOnceDeployment? deployment,
            ResolvedClickOnceApplication application,
            IEnumerable<ResolvedClickOnceAdjacentExecutable> adjacentExecutables,
            IEnumerable<ClickOnceManifestDiagnostic> diagnostics)
        {
            ArgumentNullException.ThrowIfNull(application, nameof(application));
            ArgumentNullException.ThrowIfNull(adjacentExecutables, nameof(adjacentExecutables));
            ArgumentNullException.ThrowIfNull(diagnostics, nameof(diagnostics));

            ResolvedClickOnceAdjacentExecutable[] resolvedAdjacentExecutables =
                adjacentExecutables.ToArray();

            if (deployment is null && resolvedAdjacentExecutables.Length != 0)
            {
                throw new ArgumentException(
                    "Adjacent executables require a resolved deployment.",
                    nameof(adjacentExecutables));
            }

            Deployment = deployment;
            Application = application;
            AdjacentExecutables = resolvedAdjacentExecutables;
            Diagnostics = diagnostics.ToArray();
        }

        internal ResolvedClickOnceDeployment? Deployment { get; }
        internal ResolvedClickOnceApplication Application { get; }
        internal IReadOnlyList<ResolvedClickOnceAdjacentExecutable> AdjacentExecutables { get; }
        internal IReadOnlyList<ClickOnceManifestDiagnostic> Diagnostics { get; }
    }
}
