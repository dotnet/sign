// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Azure;
using Azure.CodeSigning;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.SignatureProviders.ArtifactSigning.Test
{
    public class ArtifactSigningServiceTests
    {
        private static readonly CertificateProfileClient CertificateProfileClient = Substitute.For<CertificateProfileClient>();
        private const string AccountName = "a";
        private const string CertificateProfileName = "b";
        private static readonly ILogger<ArtifactSigningService> Logger = Substitute.For<ILogger<ArtifactSigningService>>();

        [Fact]
        public void Constructor_WhenCertificateProfileClientIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ArtifactSigningService(certificateProfileClient: null!, AccountName, CertificateProfileName, Logger));

            Assert.Equal("certificateProfileClient", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenAccountNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ArtifactSigningService(CertificateProfileClient, accountName: null!, CertificateProfileName, Logger));

            Assert.Equal("accountName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenAccountNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new ArtifactSigningService(CertificateProfileClient, accountName: string.Empty, CertificateProfileName, Logger));

            Assert.Equal("accountName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProfileNameIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ArtifactSigningService(CertificateProfileClient, AccountName, certificateProfileName: null!, Logger));

            Assert.Equal("certificateProfileName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProfileNameIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new ArtifactSigningService(CertificateProfileClient, AccountName, certificateProfileName: string.Empty, Logger));

            Assert.Equal("certificateProfileName", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ArtifactSigningService(CertificateProfileClient, AccountName, CertificateProfileName, logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public async Task GetCertificateAsync_WhenChainReturned_ReturnsLeafCertificate()
        {
            var (root, intermediate, leaf) = BuildCertificateChain();
            using (root)
            using (intermediate)
            using (leaf)
            {
                CertificateProfileClient client = CreateClientReturningChain(root, intermediate, leaf);

                using ArtifactSigningService service = new(client, AccountName, CertificateProfileName, Logger);

                using X509Certificate2 result = await service.GetCertificateAsync(CancellationToken.None);

                Assert.Equal(leaf.Thumbprint, result.Thumbprint);
            }
        }

        [Fact]
        public async Task GetAdditionalCertificatesAsync_WhenChainReturned_ReturnsNonLeafCertificates()
        {
            var (root, intermediate, leaf) = BuildCertificateChain();
            using (root)
            using (intermediate)
            using (leaf)
            {
                CertificateProfileClient client = CreateClientReturningChain(root, intermediate, leaf);

                using ArtifactSigningService service = new(client, AccountName, CertificateProfileName, Logger);

                X509Certificate2Collection result = await service.GetAdditionalCertificatesAsync(CancellationToken.None);

                Assert.Equal(2, result.Count);
                HashSet<string> thumbprints = result.Cast<X509Certificate2>().Select(c => c.Thumbprint).ToHashSet();
                Assert.Contains(root.Thumbprint, thumbprints);
                Assert.Contains(intermediate.Thumbprint, thumbprints);
                Assert.DoesNotContain(leaf.Thumbprint, thumbprints);
            }
        }

        private static CertificateProfileClient CreateClientReturningChain(params X509Certificate2[] certificates)
        {
            X509Certificate2Collection collection = new(certificates);
            byte[] pkcs7 = collection.Export(X509ContentType.Pkcs7)!;

            Response<Stream> response = Response.FromValue<Stream>(
                new MemoryStream(pkcs7),
                Substitute.For<Response>());

            CertificateProfileClient client = Substitute.For<CertificateProfileClient>();
            client
                .GetSignCertificateChainAsync(AccountName, CertificateProfileName, Arg.Any<CancellationToken>())
                .Returns(response);

            return client;
        }

        /// <summary>
        /// Builds a certificate chain with a root, intermediate, and leaf certificate.
        /// </summary>
        private static (X509Certificate2 root, X509Certificate2 intermediate, X509Certificate2 leaf) BuildCertificateChain()
        {
            DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddDays(-1);
            DateTimeOffset notAfter = DateTimeOffset.UtcNow.AddDays(1);

            using RSA rootKey = RSA.Create(2048);
            CertificateRequest rootRequest = new("CN=Test Root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, critical: true));
            X509Certificate2 root = rootRequest.CreateSelfSigned(notBefore, notAfter);

            using RSA intermediateKey = RSA.Create(2048);
            CertificateRequest intermediateRequest = new("CN=Test Intermediate", intermediateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            intermediateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
            intermediateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, critical: true));
            X509Certificate2 intermediateNoKey = intermediateRequest.Create(root, notBefore, notAfter, [1]);
            X509Certificate2 intermediate = intermediateNoKey.CopyWithPrivateKey(intermediateKey);
            intermediateNoKey.Dispose();

            using RSA leafKey = RSA.Create(2048);
            CertificateRequest leafRequest = new("CN=Test Leaf", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            X509Certificate2 leafNoKey = leafRequest.Create(intermediate, notBefore, notAfter, [2]);
            X509Certificate2 leaf = leafNoKey.CopyWithPrivateKey(leafKey);
            leafNoKey.Dispose();

            return (root, intermediate, leaf);
        }
    }
}
