// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class ResolvedClickOnceApplication
    {
        internal ResolvedClickOnceApplication(
            FileInfo source,
            IApplicationManifest manifest,
            IEnumerable<ResolvedClickOncePayload> payloads)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(source));
            ArgumentNullException.ThrowIfNull(manifest, nameof(manifest));
            ArgumentNullException.ThrowIfNull(payloads, nameof(payloads));

            Source = source;
            Manifest = manifest;
            Payloads = payloads.ToArray();
        }

        internal FileInfo Source { get; }
        internal IApplicationManifest Manifest { get; }
        internal IReadOnlyList<ResolvedClickOncePayload> Payloads { get; }
    }
}
