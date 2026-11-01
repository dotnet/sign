// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SigningOperationPlan
    {
        internal SigningOperationPlan(
            SigningFile source,
            FileInfo output)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(source));
            ArgumentNullException.ThrowIfNull(output, nameof(output));

            Source = source;
            Output = output;
        }

        internal SigningFile Source { get; }
        internal FileInfo Output { get; }
    }
}
