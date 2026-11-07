// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    // Each output names one coordinated primary and its destination in the
    // parent's layout. Siblings are not implicitly imported from its snapshot.
    internal sealed class ClickOncePayloadSigningOutput
    {
        internal ClickOncePayloadSigningOutput(
            SigningOperationPlan plan,
            SigningOperationResult result)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(result);
            Plan = plan;
            Result = result;
        }

        internal SigningOperationPlan Plan { get; }
        internal SigningOperationResult Result { get; }
    }
}
