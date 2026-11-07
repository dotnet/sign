// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal interface IClickOncePayloadSigningHandler
    {
        bool CanSign(ClickOncePayloadSigningRequest request, SignOptions options);

        // The handler coordinates its work using the original source identity.
        // Actual result waits use coordinator.WaitAsync. It returns borrowed
        // coordinator results, never ownership of snapshots.
        Task<ClickOncePayloadSigningResult> SignAsync(
            ClickOncePayloadSigningRequest request,
            SignOptions options,
            SigningOperationCoordinator coordinator);
    }
}
