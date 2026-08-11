// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class ClickOnceFileGraphStagingException : Exception
    {
        internal ClickOnceFileGraphStagingException(
            string message,
            IEnumerable<ClickOnceManifestDiagnostic> diagnostics,
            Exception? innerException = null)
            : base(message, innerException)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));
            ArgumentNullException.ThrowIfNull(diagnostics, nameof(diagnostics));

            Diagnostics = diagnostics.ToArray();
        }

        internal IReadOnlyList<ClickOnceManifestDiagnostic> Diagnostics { get; }
    }
}
