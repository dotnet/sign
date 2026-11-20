// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class ClickOnceApplicationSigningContext
    {
        private readonly HashSet<SigningSourceIdentity> _explicitPayloads;

        // Orchestration registers independently selected operations before
        // starting applications that consume them. This context never claims work.
        internal ClickOnceApplicationSigningContext(IEnumerable<SigningSourceIdentity> explicitPayloads)
        {
            ArgumentNullException.ThrowIfNull(explicitPayloads);
            SigningSourceIdentity[] identities = explicitPayloads.ToArray();
            if (identities.Any(identity => identity is null))
            {
                throw new ArgumentException("Explicit payload identities cannot contain null.", nameof(explicitPayloads));
            }
            _explicitPayloads = new HashSet<SigningSourceIdentity>(identities);
        }

        internal bool IsExplicitPayload(SigningSourceIdentity identity) => _explicitPayloads.Contains(identity);
    }
}
