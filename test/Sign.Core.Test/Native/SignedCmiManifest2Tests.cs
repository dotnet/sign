// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Deployment.Internal.CodeSigning;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Xml;

namespace Sign.Core.Test
{
    [Collection(SigningTestsCollection.Name)]
    public class SignedCmiManifest2Tests
    {
        private readonly CertificatesFixture _certificatesFixture;

        public SignedCmiManifest2Tests(CertificatesFixture certificatesFixture)
        {
            ArgumentNullException.ThrowIfNull(certificatesFixture, nameof(certificatesFixture));

            _certificatesFixture = certificatesFixture;
        }

        [Fact]
        [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
        public void Sign_Never_GeneratesSha1MessageImprint()
        {
            using (X509Certificate2 certificate = CreateCertificate())
            using (RSA privateKey = certificate.GetRSAPrivateKey()!)
            {
                XmlDocument manifest = new() { PreserveWhitespace = true };

                using (StringReader reader = new(@$"<?xml version=""1.0"" encoding=""utf-8""?>
    <asmv1:assembly xsi:schemaLocation=""urn:schemas-microsoft-com:asm.v1 assembly.adaptive.xsd"" manifestVersion=""1.0"" xmlns:asmv1=""urn:schemas-microsoft-com:asm.v1"" xmlns=""urn:schemas-microsoft-com:asm.v2"" xmlns:asmv2=""urn:schemas-microsoft-com:asm.v2"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:co.v1=""urn:schemas-microsoft-com:clickonce.v1"" xmlns:asmv3=""urn:schemas-microsoft-com:asm.v3"" xmlns:dsig=""http://www.w3.org/2000/09/xmldsig#"" xmlns:co.v2=""urn:schemas-microsoft-com:clickonce.v2"">
      <assemblyIdentity name=""WinFormsApp.application"" version=""1.0.0.0"" publicKeyToken=""46deb9a9283e4567"" language=""neutral"" processorArchitecture=""msil"" xmlns=""urn:schemas-microsoft-com:asm.v1"" />
      <publisherIdentity name=""{certificate.Subject}"" />
    </asmv1:assembly>"))
                {
                    manifest.Load(reader);
                }

                SignedCmiManifest2 signedCmiManifest = new(manifest);
                CmiManifestSigner2 signer = new(privateKey, certificate)
                {
                    Flag = CmiManifestSignerFlag.DontReplacePublicKeyToken
                };

                using (TemporaryEnvironmentPathOverride temporaryEnvironmentPath = CreateTemporaryEnvironmentPathOverride())
                {
                    signedCmiManifest.Sign(signer, _certificatesFixture.TimestampServiceUrl.AbsoluteUri);
                }

                byte[] bytes = GetTimestampBytes(manifest);

                Assert.True(
                    Rfc3161TimestampToken.TryDecode(
                        bytes,
                        out Rfc3161TimestampToken? timestampToken,
                        out int bytesConsumed));
                Assert.True(timestampToken.TokenInfo.HashAlgorithmId.IsEqualTo(Oids.Sha256));
            }
        }

        private static X509Certificate2 CreateCertificate()
        {
            RSA keyPair = RSA.Create(keySizeInBits: 3072);
            CertificateRequest request = new(
                "CN=Common Name, O=Organization, L=City, S=State, C=Country",
                keyPair,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            DateTimeOffset now = DateTimeOffset.Now;

            return request.CreateSelfSigned(now.AddMinutes(-5), now.AddMinutes(5));
        }

        private static TemporaryEnvironmentPathOverride CreateTemporaryEnvironmentPathOverride()
        {
            string windir = Environment.GetEnvironmentVariable("windir")!;
            string netfxDir = $@"{windir}\Microsoft.NET\Framework64\v4.0.30319";

            return new TemporaryEnvironmentPathOverride(netfxDir);
        }

        private static byte[] GetTimestampBytes(XmlDocument manifest)
        {
            XmlNamespaceManager namespaceManager = new(manifest.NameTable);

            namespaceManager.AddNamespace("as", "http://schemas.microsoft.com/windows/pki/2005/Authenticode");

            XmlNode? node = manifest.SelectSingleNode("//as:Timestamp", namespaceManager);

            Assert.NotNull(node);

            return Convert.FromBase64String(node.InnerText);
        }
    }
}