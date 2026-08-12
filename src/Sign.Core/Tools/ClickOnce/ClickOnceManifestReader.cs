// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Xml;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ClickOnceManifestReader : IClickOnceManifestReader
    {
        private const string AssemblyV2Namespace =
            "urn:schemas-microsoft-com:asm.v2";
        private const string SignatureNamespace =
            "http://www.w3.org/2000/09/xmldsig#";

        public bool TryReadApplicationManifest(
            Stream stream,
            [NotNullWhen(true)] out IApplicationManifest? manifest)
        {
            Manifest readManifest = ReadManifest(stream);

            if (readManifest is not ApplicationManifest applicationManifest)
            {
                readManifest.InputStream.Dispose();
                stream.Position = 0;
                manifest = null;

                return false;
            }

            ReplaceManifestInputStream(applicationManifest);
            manifest = new ApplicationManifestAdapter(applicationManifest);

            return true;
        }

        public bool TryReadDeployManifest(
            Stream stream,
            [NotNullWhen(true)] out IDeployManifest? manifest)
        {
            Manifest readManifest = ReadManifest(stream);

            if (readManifest is not DeployManifest deployManifest)
            {
                readManifest.InputStream.Dispose();
                stream.Position = 0;
                manifest = null;

                return false;
            }

            ReplaceManifestInputStream(deployManifest);
            manifest = new DeployManifestAdapter(deployManifest);

            return true;
        }

        private static Manifest ReadManifest(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream, nameof(stream));

            if (!stream.CanRead)
            {
                throw new ArgumentException(
                    "The manifest stream must be readable.",
                    nameof(stream));
            }

            if (!stream.CanSeek)
            {
                throw new ArgumentException(
                    "The manifest stream must be seekable.",
                    nameof(stream));
            }

            if (stream.Position != 0)
            {
                throw new ArgumentException(
                    "The manifest stream must be positioned at the beginning.",
                    nameof(stream));
            }

            return ManifestReader.ReadManifest(stream, preserveStream: true);
        }

        private static void ReplaceManifestInputStream(Manifest manifest)
        {
            Stream input = manifest.InputStream;
            Stream? output = null;

            try
            {
                output = CreateSanitizedManifestInputStream(input);
                manifest.InputStream = output;
                output = null;
            }
            finally
            {
                output?.Dispose();
                input.Dispose();
            }
        }

        private static Stream CreateSanitizedManifestInputStream(Stream input)
        {
            input.Position = 0;
            MemoryStream output = new();
            XmlReaderSettings readerSettings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            XmlWriterSettings writerSettings = new()
            {
                CloseOutput = false,
                Encoding = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                Indent = false
            };

            try
            {
                using XmlReader reader =
                    XmlReader.Create(input, readerSettings);
                XmlDocument document = new()
                {
                    PreserveWhitespace = true,
                    XmlResolver = null
                };
                document.Load(reader);

                XmlElement root = document.DocumentElement ??
                    throw new XmlException(
                        "The manifest XML does not have a document element.");

                for (XmlNode? node = root.FirstChild;
                    node is not null;)
                {
                    XmlNode? nextNode = node.NextSibling;

                    if (node is XmlElement element &&
                        IsStaleSigningElement(element))
                    {
                        root.RemoveChild(node);
                    }

                    node = nextNode;
                }

                using (XmlWriter writer =
                    XmlWriter.Create(output, writerSettings))
                {
                    document.Save(writer);
                }

                output.Position = 0;

                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }

        private static bool IsStaleSigningElement(XmlElement element)
        {
            bool isPublisherIdentity =
                element.LocalName == "publisherIdentity" &&
                element.NamespaceURI == AssemblyV2Namespace;
            bool isSignature =
                element.LocalName == "Signature" &&
                element.NamespaceURI == SignatureNamespace;

            return isPublisherIdentity || isSignature;
        }
    }
}
