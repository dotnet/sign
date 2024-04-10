// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;

namespace Sign.Core.Test
{
    internal static class OidExtensions
    {
        internal static bool IsEqualTo(this Oid oid, Oid other)
        {
            ArgumentNullException.ThrowIfNull(oid, nameof(oid));
            ArgumentNullException.ThrowIfNull(other, nameof(other));

            return string.Equals(oid.Value, other.Value, StringComparison.Ordinal);
        }
    }
}