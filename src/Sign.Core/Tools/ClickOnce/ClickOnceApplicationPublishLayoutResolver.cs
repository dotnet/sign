// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;

namespace Sign.Core
{
    internal sealed class ClickOnceApplicationPublishLayoutResolver
    {
        private readonly IClickOnceManifestReader _manifestReader;
        private readonly ClickOncePayloadResolver _payloadResolver;

        internal ClickOnceApplicationPublishLayoutResolver(
            IClickOnceManifestReader manifestReader,
            ClickOncePayloadResolver payloadResolver)
        {
            ArgumentNullException.ThrowIfNull(manifestReader, nameof(manifestReader));
            ArgumentNullException.ThrowIfNull(payloadResolver, nameof(payloadResolver));

            _manifestReader = manifestReader;
            _payloadResolver = payloadResolver;
        }

        internal bool TryResolve(FileInfo applicationManifestFile, out ResolvedClickOncePublishLayout? layout)
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
                    layout = null;

                    return false;
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                System.Xml.XmlException)
            {
                throw new ClickOncePublishLayoutResolutionException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceApplicationManifestReadFailed,
                        applicationManifestFile.FullName),
                    innerException: exception);
            }

            applicationManifest.ReadOnly = false;

            List<ClickOnceManifestDiagnostic> diagnostics = new();
            IReadOnlyList<ResolvedClickOncePayload> payloads = _payloadResolver.ResolveForExplicitApplication(
                applicationManifestFile,
                applicationManifest,
                diagnostics);

            layout = new ResolvedClickOncePublishLayout(
                deployment: null,
                new ResolvedClickOnceApplication(
                    applicationManifestFile,
                    applicationManifest,
                    payloads),
                adjacentExecutables: Array.Empty<ResolvedClickOnceAdjacentExecutable>(),
                diagnostics);

            return true;
        }
    }
}
