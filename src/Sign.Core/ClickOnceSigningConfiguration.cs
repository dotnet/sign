// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class ClickOnceSigningConfiguration
    {
        internal int Version { get; }
        internal bool UpdateManifest { get; }
        internal ClickOnceApplicationSigningPolicy ApplicationPolicy { get; }
        internal int MaxConcurrency { get; }

        internal ClickOnceSigningConfiguration(
            int version = 1,
            bool noSignDependencies = false,
            bool noUpdateManifest = false,
            int maxConcurrency = 4)
        {
            if (version is not (1 or 2))
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }
            if ((noSignDependencies || noUpdateManifest) && version != 2 ||
                noSignDependencies && noUpdateManifest)
            {
                throw new ArgumentException("Invalid ClickOnce signing option combination.");
            }

            Version = version;
            ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
            MaxConcurrency = maxConcurrency;
            UpdateManifest = !noUpdateManifest;
            ApplicationPolicy = noSignDependencies
                ? ClickOnceApplicationSigningPolicy.UpdateManifestOnly
                : ClickOnceApplicationSigningPolicy.SignPayloads;
        }
    }
}
