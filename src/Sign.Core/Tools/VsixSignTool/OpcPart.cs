// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.IO.Compression;
using System.Xml.Linq;

namespace Sign.Core
{
    /// <summary>
    /// Represents a part inside of a package.
    /// </summary>
    internal sealed class OpcPart : IEquatable<OpcPart>
    {
        internal OpcRelationships? _relationships;
        private readonly OpcPackageFileMode _mode;
        private readonly string _path;

        internal OpcPart(OpcPackage package, string path, ZipArchiveEntry entry, OpcPackageFileMode mode)
        {
            Uri = new Uri(OpcPackage.BasePackageUri, path);
            Package = package;
            _path = path;
            Entry = entry;
            _mode = mode;
        }

        internal OpcPackage Package { get; }

        internal ZipArchiveEntry Entry { get; }


        /// <summary>
        /// A package URI of the current part.
        /// </summary>
        public Uri Uri { get; }

        /// <summary>
        /// The collection of relationships for this part.
        /// </summary>
        public OpcRelationships Relationships
        {
            get
            {
                if (_relationships == null)
                {
                    _relationships = ConstructRelationships();
                }

                return _relationships;
            }
        }

        /// <summary>
        /// Gets a MIME content type of the current part.
        /// </summary>
        public string ContentType
        {
            get
            {
                var extension = Path.GetExtension(_path)?.TrimStart('.');

                return Package.ContentTypes.FirstOrDefault(ct => string.Equals(ct.Extension, extension, StringComparison.OrdinalIgnoreCase))?.ContentType ?? OpcKnownMimeTypes.OctetString;
            }
        }

        private string GetRelationshipFilePath()
        {
            var pathDirectory = Path.GetDirectoryName(_path);

            if (pathDirectory == null)
            {
                throw new DirectoryNotFoundException($"Cannot access parent directory of {_path}");
            }

            return Path.Combine(pathDirectory, "_rels/" + Path.GetFileName(_path) + ".rels").Replace('\\', '/');
        }

        private OpcRelationships ConstructRelationships()
        {
            var path = GetRelationshipFilePath();
            var entry = Package.Archive.GetEntry(path);
            var readOnlyMode = _mode != OpcPackageFileMode.ReadWrite;
            var location = new Uri(OpcPackage.BasePackageUri, path);
            if (entry == null)
            {
                return new OpcRelationships(location, readOnlyMode);
            }
            else
            {
                using (var stream = entry.Open())
                {
                    return new OpcRelationships(location, XDocument.Load(stream, LoadOptions.PreserveWhitespace), readOnlyMode);
                }
            }
        }


        /// <summary>
        /// Opens the part's contents as a stream.
        /// </summary>
        /// <returns>A stream of the part's contents.</returns>
        public Stream Open() => Entry.Open();

        /// <inheritdoc />
        public bool Equals(OpcPart? other) => other != null && Uri.Equals(other.Uri);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is OpcPart part && Equals(part);

        /// <inheritdoc />
        public override int GetHashCode() => Uri.GetHashCode();
    }
}