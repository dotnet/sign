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

            applicationManifest.InputStream = SanitizePreservedInput(
                applicationManifest.InputStream);
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

            deployManifest.InputStream = SanitizePreservedInput(
                deployManifest.InputStream);
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

        private static Stream SanitizePreservedInput(Stream input)
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
                using XmlWriter writer =
                    XmlWriter.Create(output, writerSettings);

                bool advanceReader = true;

                while (!reader.EOF)
                {
                    if (advanceReader && !reader.Read())
                    {
                        break;
                    }

                    advanceReader = true;

                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (reader.Depth == 1 &&
                            IsStaleSigningElement(reader))
                        {
                            reader.Skip();
                            advanceReader = false;
                            continue;
                        }

                        writer.WriteStartElement(
                            reader.Prefix,
                            reader.LocalName,
                            reader.NamespaceURI);

                        if (reader.HasAttributes)
                        {
                            writer.WriteAttributes(reader, defattr: true);
                        }

                        if (reader.IsEmptyElement)
                        {
                            writer.WriteEndElement();
                        }
                    }
                    else
                    {
                        WriteNode(reader, writer);
                    }
                }

                writer.Flush();
                output.Position = 0;
                input.Dispose();

                return output;
            }
            catch
            {
                input.Dispose();
                output.Dispose();
                throw;
            }
        }

        private static bool IsStaleSigningElement(XmlReader reader)
        {
            bool isPublisherIdentity =
                reader.LocalName == "publisherIdentity" &&
                reader.NamespaceURI == AssemblyV2Namespace;
            bool isSignature =
                reader.LocalName == "Signature" &&
                reader.NamespaceURI == SignatureNamespace;

            return isPublisherIdentity || isSignature;
        }

        private static void WriteNode(XmlReader reader, XmlWriter writer)
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.EndElement:
                    writer.WriteFullEndElement();
                    break;

                case XmlNodeType.Text:
                    writer.WriteString(reader.Value);
                    break;

                case XmlNodeType.CDATA:
                    writer.WriteCData(reader.Value);
                    break;

                case XmlNodeType.Comment:
                    writer.WriteComment(reader.Value);
                    break;

                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    writer.WriteWhitespace(reader.Value);
                    break;

                case XmlNodeType.ProcessingInstruction:
                    writer.WriteProcessingInstruction(
                        reader.Name,
                        reader.Value);
                    break;
            }
        }
    }
}
