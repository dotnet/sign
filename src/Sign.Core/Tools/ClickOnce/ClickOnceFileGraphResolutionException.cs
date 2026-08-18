// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class ClickOnceFileGraphResolutionException : Exception
    {
        internal ClickOnceFileGraphResolutionException(
            string message,
            IEnumerable<ClickOnceManifestDiagnostic>? diagnostics = null,
            Exception? innerException = null)
            : base(message, innerException)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));

            Diagnostics = diagnostics?.ToArray() ?? Array.Empty<ClickOnceManifestDiagnostic>();
        }

        internal IReadOnlyList<ClickOnceManifestDiagnostic> Diagnostics { get; }
    }
}
