// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class DistinguishedNameParserTests
    {
        [Fact]
        public void Parse_WhenSubjectIsValid_ReturnsRelativeDistinguishedNames()
        {
            string subject = "CN=Microsoft Code Signing PCA 2011, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";

            Dictionary<string, List<string>> result = DistinguishedNameParser.Parse(subject);

            Assert.Collection(
                result,
                element =>
                {
                    Assert.Equal("CN", element.Key);
                    Assert.Equal("Microsoft Code Signing PCA 2011", Assert.Single(element.Value));
                },
                element =>
                {
                    Assert.Equal("O", element.Key);
                    Assert.Equal("Microsoft Corporation", Assert.Single(element.Value));
                },
                element =>
                {
                    Assert.Equal("L", element.Key);
                    Assert.Equal("Redmond", Assert.Single(element.Value));
                },
                element =>
                {
                    Assert.Equal("S", element.Key);
                    Assert.Equal("Washington", Assert.Single(element.Value));
                },
                element =>
                {
                    Assert.Equal("C", element.Key);
                    Assert.Equal("US", Assert.Single(element.Value));
                });
        }
    }
}