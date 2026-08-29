// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    /// <summary>
    /// Extensions for working with URIs in OPC packages.
    /// </summary>
    internal static class UriHelpers
    {
        private static readonly Uri _packageBaseUri = new Uri("package:///", UriKind.Absolute);
        private static readonly Uri _rootedPackageBaseUri = new Uri("package:", UriKind.Absolute);

        /// <summary>
        /// Escapes a raw part or relationship path (as read from a zip entry name) so that
        /// characters which are valid in file names but reserved in URIs (such as '#', '?',
        /// and '%') do not get misinterpreted as URI delimiters (for example, a literal '#'
        /// would otherwise be parsed as introducing a URI fragment, silently truncating the
        /// path). Each '/'-delimited segment is escaped independently so the separators
        /// themselves are preserved.
        /// </summary>
        /// <param name="path">The raw, unescaped path.</param>
        /// <returns>A path with reserved characters in each segment percent-encoded.</returns>
        public static string EscapePartPath(string path)
        {
            var segments = path.Split('/');

            for (var i = 0; i < segments.Length; i++)
            {
                segments[i] = Uri.EscapeDataString(segments[i]);
            }

            return string.Join('/', segments);
        }

        /// <summary>
        /// Converts a package URI to a path within the package zip file.
        /// </summary>
        /// <param name="partUri">The URI to convert.</param>
        /// <returns>A string to the path in a zip file.</returns>
        public static string ToPackagePath(this Uri partUri)
        {
            var absolute = partUri.IsAbsoluteUri ? partUri : new Uri(_packageBaseUri, partUri);
            var pathUri = new Uri(absolute.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.Unescaped), UriKind.Absolute);
            var resolved = _packageBaseUri.MakeRelativeUri(pathUri);

            return resolved.ToString();
        }

        /// <summary>
        /// Converts a package URI to a qualified path within the package zip file, suitable
        /// for use as a URI reference in signature and relationship XML (properly escaped per
        /// RFC 3986, so reserved characters like '#' remain percent-encoded rather than being
        /// decoded back into syntax-breaking literals).
        /// </summary>
        /// <param name="partUri">The URI to convert.</param>
        /// <returns>A string to the qualified path in the zip file.</returns>
        public static string ToQualifiedPath(this Uri partUri)
        {
            var absolute = partUri.IsAbsoluteUri ? partUri : new Uri(_rootedPackageBaseUri, partUri);
            var pathUri = new Uri(absolute.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped), UriKind.Absolute);
            var resolved = _rootedPackageBaseUri.MakeRelativeUri(pathUri);

            return resolved.ToString();
        }


        /// <summary>
        /// Converts a package URI to a qualified relative path URI within the package zip file.
        /// </summary>
        /// <param name="partUri">The URI to convert.</param>
        /// <returns>A URI to the qualified path in the zip file.</returns>
        public static Uri ToQualifiedUri(this Uri partUri)
        {
            var absolute = partUri.IsAbsoluteUri ? partUri : new Uri(_rootedPackageBaseUri, partUri);
            var pathUri = new Uri(absolute.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.Unescaped), UriKind.Absolute);

            return _rootedPackageBaseUri.MakeRelativeUri(pathUri);
        }
    }
}
