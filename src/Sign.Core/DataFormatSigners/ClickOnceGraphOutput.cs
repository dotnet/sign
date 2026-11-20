// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed record ClickOnceGraphOutput(
        SigningFile Source,
        string RelativePath,
        SigningOperationResult Result,
        bool IsMapped = false,
        SigningFile? PublicationOrigin = null,
        string? OriginRelativePath = null);
}
