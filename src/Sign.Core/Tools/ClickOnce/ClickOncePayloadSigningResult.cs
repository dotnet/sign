// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class ClickOncePayloadSigningResult
    {
        internal ClickOncePayloadSigningResult(
            SigningOperationResult primary,
            IEnumerable<ClickOncePayloadSigningOutput>? additionalOutputs = null)
        {
            ArgumentNullException.ThrowIfNull(primary);
            ClickOncePayloadSigningOutput[] outputs = additionalOutputs?.ToArray() ??
                Array.Empty<ClickOncePayloadSigningOutput>();
            if (outputs.Any(output => output is null))
            {
                throw new ArgumentException(
                    "The output collection cannot contain a null element.",
                    nameof(additionalOutputs));
            }

            Primary = primary;
            AdditionalOutputs = Array.AsReadOnly(outputs);
        }

        internal SigningOperationResult Primary { get; }
        internal IReadOnlyList<ClickOncePayloadSigningOutput> AdditionalOutputs { get; }
    }
}
