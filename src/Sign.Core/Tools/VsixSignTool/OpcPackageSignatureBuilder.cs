// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Xml;

namespace Sign.Core
{
    /// <summary>
    /// A builder to sign an OPC package.
    /// </summary>
    internal sealed class OpcPackageSignatureBuilder
    {
        private readonly OpcPackage _package;
        private readonly List<OpcPart> _enqueuedParts;

        internal OpcPackageSignatureBuilder(OpcPackage package)
        {
            _enqueuedParts = new List<OpcPart>();
            _package = package;
        }

        /// <summary>
        /// Enqueues a part that will be part of the package signature.
        /// </summary>
        /// <param name="part">The part to enqueue.</param>
        public void EnqueuePart(OpcPart part) => _enqueuedParts.Add(part);

        /// <summary>
        /// Dequeues a part from the signature builder. This file will not be part of the signature.
        /// </summary>
        /// <param name="part">The part to dequeue.</param>
        /// <returns>True if the file was dequeued, otherwise false.</returns>
        public bool DequeuePart(OpcPart part) => _enqueuedParts.Remove(part);

        /// <summary>
        /// Enqueues a list of parts that are known for a standard configuration.
        /// </summary>
        /// <typeparam name="TPreset">The type of preset to enqueue.</typeparam>
        public void EnqueueNamedPreset<TPreset>() where TPreset : ISignatureBuilderPreset, new()
        {
            _enqueuedParts.AddRange(new TPreset().GetPartsForSigning(_package));
        }

        /// <summary>
        /// Creates a signature from the enqueued parts.
        /// </summary>
        /// <param name="configuration">The configuration of properties used to create the signature.
        /// See the documented of <see cref="SignConfigurationSet"/> for more information.</param>
        public OpcSignature Sign(SignConfigurationSet configuration)
        {
            var fileName = configuration.PublicCertificate.GetCertHashString() + ".psdsxs";
            var (allParts, signatureFile) = SignCore(fileName);
            var signingContext = new SigningContext(configuration);
            var fileManifest = OpcSignatureManifest.Build(signingContext, allParts);

            var builder = new XmlSignatureBuilder(signingContext);
            builder.SetFileManifest(fileManifest);

            var result = builder.Build();
            PublishSignature(result, signatureFile);

            _package.Flush();

            return new OpcSignature(signatureFile);
        }

        private static void PublishSignature(XmlDocument document, OpcPart signatureFile)
        {
            using (var copySignatureStream = signatureFile.Open())
            {
                copySignatureStream.SetLength(0L);
                using (var xmlWriter = new XmlTextWriter(copySignatureStream, System.Text.Encoding.UTF8))
                {
                    //The .NET implementation of OPC used by Visual Studio does not tollerate "white space" nodes.
                    xmlWriter.Formatting = Formatting.None;
                    document.Save(xmlWriter);
                }
            }
        }

        private (HashSet<OpcPart> partsToSign, OpcPart signaturePart) SignCore(string signatureFileName)
        {
            var originFileUri = new Uri("package:///package/services/digital-signature/origin.psdor", UriKind.Absolute);
            var signatureUriRoot = new Uri("package:///package/services/digital-signature/xml-signature/", UriKind.Absolute);
            var originFileRelationship = _package.Relationships.FirstOrDefault(r => r.Type.Equals(OpcKnownUris.DigitalSignatureOrigin));

            OpcPart originFile;
            OpcPart signatureFile;
            // Create the origin file and relationship to the origin file if needed.
            if (originFileRelationship != null)
            {
                originFile = _package.GetPart(originFileRelationship.Target) ?? _package.CreatePart(originFileUri, OpcKnownMimeTypes.DigitalSignatureOrigin);
            }
            else
            {
                originFile = _package.GetPart(originFileUri) ?? _package.CreatePart(originFileUri, OpcKnownMimeTypes.DigitalSignatureOrigin);
                _package.Relationships.Add(new OpcRelationship(originFile.Uri, OpcKnownUris.DigitalSignatureOrigin));
            }

            var signatureRelationship = originFile.Relationships.FirstOrDefault(r => r.Type.Equals(OpcKnownUris.DigitalSignatureSignature));

            if (signatureRelationship != null)
            {
                signatureFile = _package.GetPart(signatureRelationship.Target) ?? _package.CreatePart(originFileUri, OpcKnownMimeTypes.DigitalSignatureSignature);
            }
            else
            {
                var target = new Uri(signatureUriRoot, signatureFileName);
                signatureFile = _package.GetPart(target) ?? _package.CreatePart(target, OpcKnownMimeTypes.DigitalSignatureSignature);
                originFile.Relationships.Add(new OpcRelationship(target, OpcKnownUris.DigitalSignatureSignature));
            }

            _package.Flush();

            var allParts = new HashSet<OpcPart>(_enqueuedParts)
            {
                originFile,
            };

            var tempPart = _package.GetPart(_package.Relationships.DocumentUri);
            if (tempPart != null)
            {
                allParts.Add(tempPart);
            }

            tempPart = _package.GetPart(originFile.Relationships.DocumentUri);
            if (tempPart != null)
            {
                allParts.Add(tempPart);
            }

            return (allParts, signatureFile);
        }
    }
}
