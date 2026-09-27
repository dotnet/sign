// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SigningOperationGroup
    {
        internal SigningOperationGroup(
            IReadOnlyList<SigningOperationPlan> plans,
            SigningOperationScope scope)
        {
            Plans = plans;
            Scope = scope;
        }

        internal IReadOnlyList<SigningOperationPlan> Plans { get; }
        internal SigningOperationScope Scope { get; }
    }
}
