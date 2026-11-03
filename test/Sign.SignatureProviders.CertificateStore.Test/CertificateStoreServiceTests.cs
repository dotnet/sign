// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sign.Core;
using Sign.TestInfrastructure;

namespace Sign.SignatureProviders.CertificateStore.Test
{
    public class CertificateStoreServiceTests
    {
        private const string CertificateFingerprint = "a";
        private static readonly HashAlgorithmName CertificateFingerprintAlgorithm = HashAlgorithmName.SHA256;
        private const string? CryptoServiceProvider = "b";
        private const string? PrivateKeyContainer = "c";
        private const string? CertificateFilePath = null;
        private const string? CertificateFilePassword = null;
        private const bool IsMachineKeyContainer = true;
        private const bool IsInteractive = false;
        private readonly IServiceProvider serviceProvider;

        public CertificateStoreServiceTests()
        {
            ServiceCollection services = new();
            services.AddSingleton<ILogger<CertificateStoreService>>(new TestLogger<CertificateStoreService>());
            serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void Constructor_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new CertificateStoreService(serviceProvider: null!, CertificateFingerprint, CertificateFingerprintAlgorithm, CryptoServiceProvider, PrivateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer, IsInteractive));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateFingerprintIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new CertificateStoreService(serviceProvider, certificateFingerprint: null!, CertificateFingerprintAlgorithm, CryptoServiceProvider, PrivateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer, IsInteractive));

            Assert.Equal("certificateFingerprint", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateFingerprintIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new CertificateStoreService(serviceProvider, certificateFingerprint: string.Empty, CertificateFingerprintAlgorithm, CryptoServiceProvider, PrivateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer, IsInteractive));

            Assert.Equal("certificateFingerprint", exception.ParamName);
        }

        [Theory]
        [InlineData(X509ContentType.Cert, null, "binary")]
        [InlineData(X509ContentType.Pfx, null, "binary")]
        [InlineData(X509ContentType.Pfx, "test-password", "binary")]
        [InlineData(X509ContentType.Pkcs7, null, "binary")]
        [InlineData(X509ContentType.Pkcs7, null, "pem")]
        [InlineData(X509ContentType.Pkcs7, null, "base64")]
        [InlineData(X509ContentType.Pkcs7, null, "pem-certificate-label")]
        [InlineData(X509ContentType.Pkcs7, null, "pem-utf8-bom")]
        [InlineData(X509ContentType.Pkcs7, null, "pem-utf16")]
        [InlineData(X509ContentType.Pkcs7, null, "pem-utf16-bom")]
        [InlineData(X509ContentType.Pkcs7, null, "base64-utf16")]
        [InlineData(X509ContentType.Pkcs7, null, "base64-utf16-bom")]
        [InlineData(X509ContentType.Pkcs7, null, "binary-with-embedded-pem")]
        [InlineData(X509ContentType.Pkcs7, null, "pem-with-binary-prefix")]
#if !NET9_0_OR_GREATER
        [InlineData(X509ContentType.SerializedCert, null, "binary")]
        [InlineData(X509ContentType.SerializedStore, null, "binary")]
#endif
        public async Task GetCertificateAsync_WhenCertificateFileProvided_ReturnsMatchingCertificate(
            X509ContentType contentType,
            string? password,
            string encoding)
        {
            using X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate();
            using X509Certificate2 otherCertificate = SelfIssuedCertificateCreator.CreateCertificate();
            X509Certificate2Collection certificates = [otherCertificate, certificate];
            byte[] rawData = contentType is X509ContentType.Cert or X509ContentType.SerializedCert
                ? certificate.Export(contentType)
                : certificates.Export(contentType, password)!;
            rawData = encoding switch
            {
                "binary" => rawData,
                "pem" => Encoding.ASCII.GetBytes(PemEncoding.WriteString("PKCS7", rawData)),
                "base64" => Encoding.ASCII.GetBytes(Convert.ToBase64String(rawData, Base64FormattingOptions.InsertLineBreaks)),
                "pem-certificate-label" => Encoding.ASCII.GetBytes(PemEncoding.WriteString("CERTIFICATE", rawData)),
                "pem-utf8-bom" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(PemEncoding.WriteString("PKCS7", rawData))],
                "pem-utf16" => Encoding.Unicode.GetBytes(PemEncoding.WriteString("PKCS7", rawData)),
                "pem-utf16-bom" => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(PemEncoding.WriteString("PKCS7", rawData))],
                "base64-utf16" => Encoding.Unicode.GetBytes(Convert.ToBase64String(rawData)),
                "base64-utf16-bom" => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Convert.ToBase64String(rawData))],
                "binary-with-embedded-pem" => CreatePkcs7WithEmbeddedPem(certificate, otherCertificate),
                "pem-with-binary-prefix" => [.. rawData[..21], .. Encoding.ASCII.GetBytes(PemEncoding.WriteString("PKCS7", rawData))],
                _ => throw new ArgumentOutOfRangeException(nameof(encoding))
            };
            using DirectoryService directoryService = new(new TestLogger<IDirectoryService>());
            using TemporaryDirectory directory = new(directoryService);
            string path = Path.Combine(directory.Directory.FullName, "certificate");
            File.WriteAllBytes(path, rawData);

            CertificateStoreService service = new(
                serviceProvider,
                certificate.GetCertHashString(CertificateFingerprintAlgorithm),
                CertificateFingerprintAlgorithm,
                cryptoServiceProvider: null,
                privateKeyContainer: null,
                certificatePath: path,
                certificatePassword: password,
                isPrivateMachineKeyContainer: false,
                isInteractive: false);

            using X509Certificate2 result = await service.GetCertificateAsync(CancellationToken.None);

            Assert.Equal(certificate.Thumbprint, result.Thumbprint);
            if (contentType == X509ContentType.Pfx)
            {
                Assert.True(result.HasPrivateKey);
            }
        }

#if NET9_0_OR_GREATER
        [Theory]
        [InlineData(X509ContentType.SerializedCert)]
        [InlineData(X509ContentType.SerializedStore)]
        [InlineData(X509ContentType.Authenticode)]
        public async Task GetCertificateAsync_WhenCertificateFileFormatIsUnsupported_Throws(X509ContentType contentType)
        {
            using X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate();
            using DirectoryService directoryService = new(new TestLogger<IDirectoryService>());
            using TemporaryDirectory directory = new(directoryService);
            string path = Path.Combine(directory.Directory.FullName, "certificate");
            if (contentType == X509ContentType.Authenticode)
            {
                File.Copy(Path.Combine(AppContext.BaseDirectory, "tools", "SDK", "x86", "mage.exe"), path);
            }
            else
            {
                X509Certificate2Collection certificates = new(certificate);
                byte[] rawData = contentType == X509ContentType.SerializedCert
                    ? certificate.Export(contentType)
                    : certificates.Export(contentType)!;
                File.WriteAllBytes(path, rawData);
            }
            Assert.Equal(contentType, X509Certificate2.GetCertContentType(path));

            CertificateStoreService service = new(
                serviceProvider,
                certificate.GetCertHashString(CertificateFingerprintAlgorithm),
                CertificateFingerprintAlgorithm,
                cryptoServiceProvider: null,
                privateKeyContainer: null,
                certificatePath: path,
                certificatePassword: null,
                isPrivateMachineKeyContainer: false,
                isInteractive: false);

            NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
                () => service.GetCertificateAsync(CancellationToken.None));

            Assert.Contains(contentType.ToString(), exception.Message);
        }
#endif

        private static byte[] CreatePkcs7WithEmbeddedPem(X509Certificate2 certificate, X509Certificate2 embeddedCertificate)
        {
            X509Certificate2Collection embeddedCertificates = new(embeddedCertificate);
            byte[] embeddedData = embeddedCertificates.Export(X509ContentType.Pkcs7)!;
            ContentInfo content = new(Encoding.ASCII.GetBytes(PemEncoding.WriteString("PKCS7", embeddedData)));
            SignedCms signedCms = new(content);
            CmsSigner signer = new(certificate)
            {
                IncludeOption = X509IncludeOption.EndCertOnly
            };
            signedCms.ComputeSignature(signer);
            return signedCms.Encode();
        }
    }
}
