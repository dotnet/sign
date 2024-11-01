// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Xml;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    public sealed class XmlDocumentLoaderTests : IDisposable
    {
        private readonly XmlDocumentLoader _loader;
        private readonly TemporaryDirectory _temporaryDirectory;

        public XmlDocumentLoaderTests()
        {
            _loader = new XmlDocumentLoader();
            _temporaryDirectory = new TemporaryDirectory(new DirectoryService(Mock.Of<ILogger<IDirectoryService>>()));
        }

        public void Dispose()
        {
            _temporaryDirectory.Dispose();
        }

        [Fact]
        public void Load_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => _loader.Load(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Fact]
        public void Load_WhenXmlContainsAnEmbeddedDtd_Throws()
        {
            string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE note [
  <!ELEMENT note (to, from, heading, body)>
  <!ELEMENT to (#PCDATA)>
  <!ELEMENT from (#PCDATA)>
  <!ELEMENT heading (#PCDATA)>
  <!ELEMENT body (#PCDATA)>
]>
<note>
  <to>you</to>
  <from>me</from>
  <heading>Reminder</heading>
  <body>Don't forget this weekend!</body>
</note>";
            FileInfo file = CreateFile(xml);

            Assert.Throws<XmlException>(() => _loader.Load(file));
        }

        [Fact]
        public void Load_WhenXmlContainsAnExternalEntity_Throws()
        {
            string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE root [
<!ENTITY ext SYSTEM ""https://unit.test/external.dtd"">
]>
<root>&ext;</root>";
            FileInfo file = CreateFile(xml);

            Assert.Throws<XmlException>(() => _loader.Load(file));
        }

        [Fact]
        public void Load_WhenXmlIsAcceptable_ReturnsXmlDocument()
        {
            string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?><a>b</a>";
            FileInfo file = CreateFile(xml);

            XmlDocument xmlDoc = _loader.Load(file);

            Assert.Equal("a", xmlDoc.DocumentElement!.Name);
            Assert.Equal("b", xmlDoc.DocumentElement.InnerText);
        }

        private FileInfo CreateFile(string xml)
        {
            FileInfo file = new(Path.Combine(_temporaryDirectory.Directory.FullName, "test.xml"));

            File.WriteAllText(file.FullName, xml);

            file.Refresh();

            return file;
        }
    }
}
