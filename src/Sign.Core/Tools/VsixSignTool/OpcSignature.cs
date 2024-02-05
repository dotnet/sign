// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    /// <summary>
    /// Represents an OPC signature.
    /// </summary>
    /// <remarks>
    /// This type cannot be directly created. To create a signature on a package, use <see cref="OpcPackage.CreateSignatureBuilder" />.
    /// </remarks>
    internal sealed class OpcSignature
    {
        private readonly OpcPart _signaturePart;
        private bool _detached;

        internal OpcSignature(OpcPart signaturePart)
        {
            _detached = false;
            _signaturePart = signaturePart;
        }

        /// <summary>
        /// Gets the part in the package for this signatures.
        /// </summary>
        public OpcPart? Part => _detached ? null : _signaturePart;

        /// <summary>
        /// Creates a builder to timestamp the existing signature.
        /// </summary>
        /// <returns>An <see cref="OpcPackageTimestampBuilder"/> that allows building and configuring timestamps.</returns>
        public OpcPackageTimestampBuilder CreateTimestampBuilder()
        {
            if (_detached)
            {
                throw new InvalidOperationException("Cannot timestamp a signature that has been removed.");
            }

            return new OpcPackageTimestampBuilder(_signaturePart);
        }


        /// <summary>
        /// Removes the signature from the package.
        /// </summary>
        public void Remove()
        {
            if (_detached)
            {
                return;
            }

            _detached = true;
            var originFileRelationship = _signaturePart.Package.Relationships.FirstOrDefault(r => r.Type.Equals(OpcKnownUris.DigitalSignatureOrigin));

            if (originFileRelationship == null)
            {
                // This shouldn't ever happen. This means we have a signature instance but no metadata connecting it to the package.
                // all we can do at this point is delete the part.
                _signaturePart.Package.RemovePart(_signaturePart);

                return;
            }

            var originFile = _signaturePart.Package.GetPart(originFileRelationship.Target);

            if (originFile == null)
            {
                // This shouldn't happen either. The package has a relationship to a non-existing origin file. Clean up the
                // signature part and the relationship.
                _signaturePart.Package.RemovePart(_signaturePart);
                _signaturePart.Package.Relationships.Remove(originFileRelationship);

                return;
            }

            var signatureRelationships = originFile.Relationships.Where(
                r => r.Target == _signaturePart.Uri.ToQualifiedUri() &&
                     r.Type == OpcKnownUris.DigitalSignatureSignature
                ).ToList();

            if (signatureRelationships.Count == 0)
            {
                // Another case that shouldn't happen. There was an origin file, but no relationship to this signature.
                // Remove the signature. We don't remove the origin file relationship because there could be other valid
                // relationships at this point.
                _signaturePart.Package.RemovePart(_signaturePart);

                return;
            }

            // This is the valid scenario. We need to remove the signature part and the origin relationship to this
            // signature. If there are no more signatures after this one is removed, then we remove the origin file
            // and the package relationship to the origin.
            // Note that it is technically incorrect for the origin file to have more than one reference to this signature
            // with the same type, but if it did happen, we remove all of them.

            // Remove relationships to this signature.
            foreach (var signatureRelationship in signatureRelationships)
            {
                originFile.Relationships.Remove(signatureRelationship);
            }

            // If we're empty now, remove the origin file and the package relationship to the origin.
            if (originFile.Relationships.Count == 0)
            {
                _signaturePart.Package.RemovePart(originFile);
                _signaturePart.Package.Relationships.Remove(originFileRelationship);
            }
            _signaturePart.Package.RemovePart(_signaturePart);
        }
    }
}
