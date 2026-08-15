// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;

namespace Sign.Core
{
    internal sealed class ClickOnceApplicationManifestFileGraphResolver
    {
        private readonly IClickOnceManifestReader _manifestReader;
        private readonly ClickOncePayloadFileResolver _payloadResolver;

        internal ClickOnceApplicationManifestFileGraphResolver(
            IClickOnceManifestReader manifestReader,
            ClickOncePayloadFileResolver payloadResolver)
        {
            ArgumentNullException.ThrowIfNull(manifestReader, nameof(manifestReader));
            ArgumentNullException.ThrowIfNull(payloadResolver, nameof(payloadResolver));

            _manifestReader = manifestReader;
            _payloadResolver = payloadResolver;
        }

        internal bool TryResolve(FileInfo applicationManifestFile, out ClickOnceFileGraph? graph)
        {
            ArgumentNullException.ThrowIfNull(applicationManifestFile, nameof(applicationManifestFile));

            IApplicationManifest? applicationManifest;

            try
            {
                using FileStream stream = applicationManifestFile.OpenRead();

                if (!_manifestReader.TryReadApplicationManifest(
                    stream,
                    out applicationManifest))
                {
                    graph = null;

                    return false;
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                System.Xml.XmlException)
            {
                throw new ClickOnceFileGraphResolutionException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceApplicationManifestReadFailed,
                        applicationManifestFile.FullName),
                    innerException: exception);
            }

            applicationManifest.ReadOnly = false;

            List<ClickOnceManifestDiagnostic> diagnostics = new();
            IReadOnlyList<ClickOnceFileGraphEntry> payloads = _payloadResolver.ResolveForExplicitApplication(
                applicationManifestFile,
                applicationManifest,
                diagnostics);

            graph = new ClickOnceFileGraph(
                deploymentManifest: null,
                deployManifest: null,
                new ClickOnceFileGraphEntry(
                    applicationManifestFile,
                    applicationManifestFile.Name,
                    ClickOnceFileGraphEntryKind.ApplicationManifest),
                applicationManifest,
                payloads,
                adjacentExecutables: Array.Empty<ClickOnceFileGraphEntry>(),
                diagnostics);

            return true;
        }
    }
}
