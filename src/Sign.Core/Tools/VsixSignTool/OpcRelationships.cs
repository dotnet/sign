// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Sign.Core
{
    /// <summary>
    /// A class that represents a part relationship in a package.
    /// </summary>
    internal sealed class OpcRelationship : IEquatable<OpcRelationship>
    {
        /// <summary>
        /// The target part for the relationship.
        /// </summary>
        public Uri Target { get; }

        /// <summary>
        /// A unique identifier for the part.
        /// </summary>
        public string? Id { get; internal set; }

        /// <summary>
        /// The type of the part.
        /// </summary>
        public Uri Type { get; }

        /// <summary>
        /// Creates a new relationship for a part.
        /// </summary>
        /// <param name="target">The package relative URI for the target part.</param>
        /// <param name="id">A unique identifier for a part.</param>
        /// <param name="type">A URI indicating the type of the part.</param>
        public OpcRelationship(Uri target, string id, Uri type)
        {
            Target = target;
            Id = id;
            Type = type;
        }


        /// <summary>
        /// Creates a new relationship for a part.
        /// </summary>
        /// <param name="target">The package relative URI for the target part.</param>
        /// <param name="type">A URI indicating the type of the part.</param>
        public OpcRelationship(Uri target, Uri type)
        {
            Target = target;
            Type = type;
        }


        /// <summary>
        /// Compares two part relationships for equality.
        /// </summary>
        /// <param name="other">The <see cref="OpcRelationship"/> to compare against.</param>
        /// <returns>True if the relationships are equal, false otherwise.</returns>
        public bool Equals(OpcRelationship? other) => other != null && Target == other.Target && Type == other.Type && Id == other.Id;

        /// <summary>
        /// Compares against an object for equality.
        /// </summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>True if the objects are equal, false otherwise.</returns>
        public override bool Equals(object? obj) => obj is OpcRelationship rel && Equals(rel);

        /// <inheritdoc />
        public override int GetHashCode() => Target.GetHashCode() ^ Type.GetHashCode();
    }


    /// <summary>
    /// A class for a collection of <see cref="OpcRelationship"/>.
    /// </summary>
    internal sealed class OpcRelationships : IList<OpcRelationship>
    {
        private static readonly XNamespace _opcRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        private readonly List<OpcRelationship> _relationships = new List<OpcRelationship>();


        internal OpcRelationships(Uri documentUri, XDocument? document, bool isReadOnly)
        {
            IsReadOnly = isReadOnly;
            DocumentUri = documentUri;
            var relationships = document?.Root?.Elements(_opcRelationshipNamespace + "Relationship");

            if (relationships == null)
            {
                return;
            }

            foreach (var relationship in relationships)
            {
                var target = relationship.Attribute("Target")?.Value;
                var id = relationship.Attribute("Id")?.Value;
                var type = relationship.Attribute("Type")?.Value;

                if (type == null || id == null || target == null)
                {
                    continue;
                }

                _relationships.Add(new OpcRelationship(new Uri(target, UriKind.Relative), id,
                    new Uri(type, UriKind.RelativeOrAbsolute)));
            }
        }

        internal OpcRelationships(Uri documentUri, bool isReadOnly)
        {
            IsReadOnly = isReadOnly;
            DocumentUri = documentUri;
        }


        /// <summary>
        /// Creates an <see cref="XDocument"/> for the currect collection of relationships.
        /// </summary>
        /// <returns>An <see cref="XDocument"/> instance for all relationships in the collection.</returns>
        public XDocument ToXml()
        {
            var document = new XDocument();
            var root = new XElement(_opcRelationshipNamespace + "Relationships");

            foreach (var relationship in _relationships)
            {
                var element = new XElement(_opcRelationshipNamespace + "Relationship");
                element.SetAttributeValue("Target", relationship.Target.ToQualifiedPath());
                element.SetAttributeValue("Id", relationship.Id);
                element.SetAttributeValue("Type", relationship.Type);
                root.Add(element);
            }

            document.Add(root);

            return document;
        }


        /// <summary>
        /// Gets the number of relationships in the collection.
        /// </summary>
        public int Count => _relationships.Count;

        /// <summary>
        /// True if the collection is read only, otherwise false.
        /// </summary>
        public bool IsReadOnly { get; }

        internal Uri DocumentUri { get; }

        /// <summary>
        /// Gets an <see cref="OpcRelationship"/> at a given index.
        /// </summary>
        /// <param name="index">The index of the relationship.</param>
        /// <returns>An <see cref="OpcRelationship"/>.</returns>
        public OpcRelationship this[int index]
        {
            get => _relationships[index];
            set
            {
                AssertNotReadOnly();
                IsDirty = true;
                AssignRelationshipId(value);
                _relationships[index] = value;
            }
        }

        internal bool IsDirty { get; set; }


        /// <summary>
        /// Gets the index of an <see cref="OpcRelationship"/>.
        /// </summary>
        /// <param name="item">The instance of <see cref="OpcRelationship"/> to retreive the index.</param>
        /// <returns>The index of the relatnship, or <c>-1</c> if the relationship is not in this collection.</returns>
        public int IndexOf(OpcRelationship item) => _relationships.IndexOf(item);

        /// <summary>
        /// Inserts an <see cref="OpcRelationship"/> at a specific index.
        /// </summary>
        /// <param name="index">The index to insert the <see cref="OpcRelationship"/> at.</param>
        /// <param name="item">The relationship to insert.</param>
        public void Insert(int index, OpcRelationship item)
        {
            AssertNotReadOnly();
            IsDirty = true;
            AssignRelationshipId(item);
            _relationships.Insert(index, item);
        }

        /// <summary>
        /// Removes an <see cref="OpcRelationship"/> at a specific index.
        /// </summary>
        /// <param name="index">The index to remove the <see cref="OpcRelationship"/> at.</param>
        public void RemoveAt(int index)
        {
            AssertNotReadOnly();
            IsDirty = true;
            _relationships.RemoveAt(index);
        }

        /// <summary>
        /// Adds a relationship to the collection.
        /// </summary>
        /// <param name="item">The relationship to add.</param>
        public void Add(OpcRelationship item)
        {
            AssertNotReadOnly();
            IsDirty = true;
            AssignRelationshipId(item);
            _relationships.Add(item);
        }

        /// <summary>
        /// Clears the list of relationships.
        /// </summary>
        public void Clear()
        {
            AssertNotReadOnly();
            IsDirty = true;
            _relationships.Clear();
        }


        /// <summary>
        /// Determines if the collections contains a relationship.
        /// </summary>
        /// <param name="item">The item to determine if it exists in the current collection.</param>
        /// <returns>True if the item is in the collection, otherwise false.</returns>
        public bool Contains(OpcRelationship item) => _relationships.Contains(item);

        /// <inheritdoc />
        public void CopyTo(OpcRelationship[] array, int arrayIndex) => _relationships.CopyTo(array, arrayIndex);

        /// <summary>
        /// Removes a relationship from the collection.
        /// </summary>
        /// <param name="item">The item to remove.</param>
        /// <returns>True if the item was removed, otherwise false.</returns>
        public bool Remove(OpcRelationship item)
        {
            AssertNotReadOnly();

            return IsDirty = _relationships.Remove(item);
        }

        /// <inheritdoc />
        public IEnumerator<OpcRelationship> GetEnumerator() => _relationships.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void AssertNotReadOnly()
        {
            if (IsReadOnly)
            {
                throw new InvalidOperationException("Cannot update relationships in a read only package. Please open the package in write mode.");
            }
        }

        private void AssignRelationshipId(OpcRelationship relationship)
        {
            if (!string.IsNullOrWhiteSpace(relationship.Id))
            {
                return;
            }

#if NET
            Span<byte> data = stackalloc byte[sizeof(uint)];
            Span<char> buffer = stackalloc char[9];
            buffer[0] = 'R';

            while (true)
            {
                RandomNumberGenerator.Fill(data);
                if (!HexHelpers.TryHexEncode(data, buffer.Slice(1)))
                {
                    throw new InvalidOperationException("Buffer is too small.");
                }
                var id = buffer.ToString();
                if (_relationships.Any(r => r.Id == id))
                {
                    continue;
                }
                relationship.Id = id;
                break;
            }
#else
            using (var rng = RandomNumberGenerator.Create())
            {
                var data = new byte[4];
                Span<char> buffer = stackalloc char[9];
                buffer[0] = 'R';
                while(true)
                {
                    rng.GetBytes(data);
                    if (!HexHelpers.TryHexEncode(data, buffer.Slice(1)))
                    {
                        throw new InvalidOperationException("Buffer is too small.");
                    }
                    var id = buffer.ToString();
                    if (_relationships.Any(r ? => r.Id == id))
                    {
                        continue;
                    }
                    relationship.Id = id;
                    break;
                }
            }
#endif
        }
    }
}
