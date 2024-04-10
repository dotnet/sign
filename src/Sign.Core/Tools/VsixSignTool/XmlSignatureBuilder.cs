// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace Sign.Core
{
    internal class XmlSignatureBuilder
    {
        private readonly XmlDocument _document;
        private readonly ISigningContext _signingContext;
        private readonly XmlElement _signatureElement;
        private XmlElement? _objectElement;


        /// <summary>
        /// Creates a new signature with the correct namespace and empty root <c>Signature</c> element.
        /// </summary>
        internal XmlSignatureBuilder(ISigningContext signingContext)
        {
            _signingContext = signingContext;
            _document = new XmlDocument();
            var manager = new XmlNamespaceManager(_document.NameTable);
            manager.AddNamespace(prefix: "", OpcKnownUris.XmlDSig.AbsoluteUri);
            _signatureElement = CreateDSigElement("Signature");
        }

        private XmlElement CreateDSigElement(string name) => _document.CreateElement(name, OpcKnownUris.XmlDSig.AbsoluteUri);

        public XmlDocument Build()
        {
            if (_objectElement == null)
            {
                throw new InvalidOperationException("A manifest has not been set on the builder.");
            }

            XmlElement keyInfoElement, signedInfo, signatureValue;
            var info = new HashAlgorithmInfo(_signingContext.FileDigestAlgorithmName);

            using (var canonicalHashAlgorithm = info.Create())
            {
                byte[] objectElementHash;
                string canonicalizationMethodObjectId;

                using (var objectElementCanonicalData = CanonicalizeElement(_objectElement, out canonicalizationMethodObjectId))
                {
                    objectElementHash = canonicalHashAlgorithm.ComputeHash(objectElementCanonicalData);
                }

                keyInfoElement = BuildKeyInfoElement();
                Stream signerInfoCanonicalStream;
                (signerInfoCanonicalStream, signedInfo) =
                    BuildSignedInfoElement(
                        (_objectElement, objectElementHash, info.XmlDSigIdentifier.AbsoluteUri, canonicalizationMethodObjectId)
                    );
                byte[] signerInfoElementHash;

                using (signerInfoCanonicalStream)
                {
                    signerInfoElementHash = canonicalHashAlgorithm.ComputeHash(signerInfoCanonicalStream);
                }

                signatureValue = BuildSignatureValue(signerInfoElementHash);
            }

            _signatureElement.AppendChild(signedInfo);
            _signatureElement.AppendChild(signatureValue);
            _signatureElement.AppendChild(keyInfoElement);
            _signatureElement.AppendChild(_objectElement);
            _document.AppendChild(_signatureElement);

            return _document;
        }

        private XmlElement BuildSignatureValue(byte[] signerInfoElementHash)
        {
            var signatureValueElement = CreateDSigElement("SignatureValue");
            signatureValueElement.InnerText = Convert.ToBase64String(_signingContext.SignDigest(signerInfoElementHash));

            return signatureValueElement;
        }

        private Stream CanonicalizeElement(XmlElement element, out string canonicalizationMethodUri, Action<string>? setCanonicalization = null)
        {
            //The canonicalization transformer can't reasonable do just an element. It
            //seems content to do an entire XmlDocument.

            var transformer = new XmlDsigC14NTransform(false);
            string? algorithm = transformer.Algorithm;

            if (!string.IsNullOrEmpty(algorithm))
            {
                setCanonicalization?.Invoke(algorithm);

                var newDocument = new XmlDocument(_document.NameTable);
                newDocument.LoadXml(element.OuterXml);

                transformer.LoadInput(newDocument);

                var result = transformer.GetOutput(typeof(Stream));
                canonicalizationMethodUri = algorithm;
                if (result is Stream s)
                {
                    return s;
                }
            }

            throw new NotSupportedException("Unable to canonicalize element.");
        }

        private (Stream, XmlElement) BuildSignedInfoElement(params (XmlElement element, byte[] canonicalDigest, string digestAlgorithm, string canonicalizationMethod)[] objects)
        {
            var signingIdentifier = _signingContext.XmlDSigIdentifier;

            var signedInfoElement = CreateDSigElement("SignedInfo");
            var canonicalizationMethodElement = CreateDSigElement("CanonicalizationMethod");
            var canonicalizationMethodAlgorithmAttribute = _document.CreateAttribute("Algorithm");
            canonicalizationMethodElement.Attributes.Append(canonicalizationMethodAlgorithmAttribute);

            var signatureMethodElement = CreateDSigElement("SignatureMethod");
            var signatureMethodAlgorithmAttribute = _document.CreateAttribute("Algorithm");
            signatureMethodAlgorithmAttribute.Value = signingIdentifier.AbsoluteUri;
            signatureMethodElement.Attributes.Append(signatureMethodAlgorithmAttribute);

            signedInfoElement.AppendChild(canonicalizationMethodElement);
            signedInfoElement.AppendChild(signatureMethodElement);

            foreach (var (element, digest, digestAlgorithm, method) in objects)
            {
                var idFromElement = element.GetAttribute("Id");
                var reference = "#" + idFromElement;

                var referenceElement = CreateDSigElement("Reference");
                var referenceUriAttribute = _document.CreateAttribute("URI");
                var referenceTypeAttribute = _document.CreateAttribute("Type");
                referenceUriAttribute.Value = reference;
                referenceTypeAttribute.Value = OpcKnownUris.XmlDSigObject.AbsoluteUri;

                referenceElement.Attributes.Append(referenceUriAttribute);
                referenceElement.Attributes.Append(referenceTypeAttribute);

                var referencesTransformsElement = CreateDSigElement("Transforms");
                var transformElement = CreateDSigElement("Transform");
                var transformAlgorithmAttribute = _document.CreateAttribute("Algorithm");
                transformAlgorithmAttribute.Value = method;
                transformElement.Attributes.Append(transformAlgorithmAttribute);
                referencesTransformsElement.AppendChild(transformElement);
                referenceElement.AppendChild(referencesTransformsElement);

                var digestMethodElement = CreateDSigElement("DigestMethod");
                var digestMethodAlgorithmAttribute = _document.CreateAttribute("Algorithm");
                digestMethodAlgorithmAttribute.Value = digestAlgorithm;
                digestMethodElement.Attributes.Append(digestMethodAlgorithmAttribute);
                referenceElement.AppendChild(digestMethodElement);

                var digestValueElement = CreateDSigElement("DigestValue");
                digestValueElement.InnerText = Convert.ToBase64String(digest);
                referenceElement.AppendChild(digestValueElement);

                signedInfoElement.AppendChild(referenceElement);
            }

            var canonicalSignerInfo = CanonicalizeElement(signedInfoElement, out _, c => canonicalizationMethodAlgorithmAttribute.Value = c);

            return (canonicalSignerInfo, signedInfoElement);
        }

        private XmlElement BuildKeyInfoElement()
        {
            var publicCertificate = Convert.ToBase64String(_signingContext.Certificate.Export(X509ContentType.Cert));
            var keyInfoElement = CreateDSigElement("KeyInfo");
            var x509DataElement = CreateDSigElement("X509Data");
            var x509CertificateElement = CreateDSigElement("X509Certificate");
            x509CertificateElement.InnerText = publicCertificate;
            x509DataElement.AppendChild(x509CertificateElement);
            keyInfoElement.AppendChild(x509DataElement);

            return keyInfoElement;
        }

        public void SetFileManifest(OpcSignatureManifest manifest)
        {
            var objectElement = CreateDSigElement("Object");
            var objectElementId = _document.CreateAttribute("Id");
            objectElementId.Value = "idPackageObject";
            objectElement.Attributes.Append(objectElementId);

            var manifestElement = CreateDSigElement("Manifest");

            foreach (var file in manifest.Manifest)
            {
                var referenceElement = CreateDSigElement("Reference");
                var referenceElementUriAttribute = _document.CreateAttribute("URI");
                referenceElementUriAttribute.Value = file.ReferenceUri.ToQualifiedPath();
                referenceElement.Attributes.Append(referenceElementUriAttribute);

                var digestMethod = CreateDSigElement("DigestMethod");
                var digestMethodAlgorithmAttribute = _document.CreateAttribute("Algorithm");
                digestMethodAlgorithmAttribute.Value = file.DigestAlgorithmIdentifier.AbsoluteUri;
                digestMethod.Attributes.Append(digestMethodAlgorithmAttribute);
                referenceElement.AppendChild(digestMethod);

                var digestValue = CreateDSigElement("DigestValue");
                digestValue.InnerText = System.Convert.ToBase64String(file.Digest);
                referenceElement.AppendChild(digestValue);


                manifestElement.AppendChild(referenceElement);
                objectElement.AppendChild(manifestElement);
            }

            var signaturePropertiesElement = CreateDSigElement("SignatureProperties");
            var signaturePropertyElement = CreateDSigElement("SignatureProperty");
            var signaturePropertyIdAttribute = _document.CreateAttribute("Id");
            var signaturePropertyTargetAttribute = _document.CreateAttribute("Target");
            signaturePropertyIdAttribute.Value = "idSignatureTime";
            signaturePropertyTargetAttribute.Value = "";

            signaturePropertyElement.Attributes.Append(signaturePropertyIdAttribute);
            signaturePropertyElement.Attributes.Append(signaturePropertyTargetAttribute);

            var signatureTimeElement = _document.CreateElement("SignatureTime", OpcKnownUris.XmlDigitalSignature.AbsoluteUri);
            var signatureTimeFormatElement = _document.CreateElement("Format", OpcKnownUris.XmlDigitalSignature.AbsoluteUri);
            var signatureTimeValueElement = _document.CreateElement("Value", OpcKnownUris.XmlDigitalSignature.AbsoluteUri);
            signatureTimeFormatElement.InnerText = "YYYY-MM-DDThh:mm:ss.sTZD";
            signatureTimeValueElement.InnerText = _signingContext.ContextCreationTime.ToString("yyyy-MM-ddTHH:mm:ss.fzzz");

            signatureTimeElement.AppendChild(signatureTimeFormatElement);
            signatureTimeElement.AppendChild(signatureTimeValueElement);

            signaturePropertyElement.AppendChild(signatureTimeElement);
            signaturePropertiesElement.AppendChild(signaturePropertyElement);
            objectElement.AppendChild(signaturePropertiesElement);

            _objectElement = objectElement;
        }
    }
}
