// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Xml;

namespace Sign.Core
{
    internal sealed class XmlDocumentLoader : IXmlDocumentLoader
    {
        public XmlDocument Load(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file);

            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null // prevent external entity resolution
            };
            XmlDocument xmlDoc = new();

            using (XmlReader reader = XmlReader.Create(file.FullName, settings))
            {
                xmlDoc.Load(reader);
            }

            return xmlDoc;
        }
    }
}
