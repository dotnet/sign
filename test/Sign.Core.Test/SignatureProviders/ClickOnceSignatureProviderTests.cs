// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    public sealed class ClickOnceSignatureProviderTests : IDisposable
    {
        private readonly DirectoryService _directoryService = new(Mock.Of<ILogger<IDirectoryService>>());
        private readonly ClickOnceSignatureProvider _provider;

        public ClickOnceSignatureProviderTests()
        {
            _provider = new ClickOnceSignatureProvider(
                Mock.Of<IKeyVaultService>(),
                Mock.Of<IContainerProvider>(),
                Mock.Of<IServiceProvider>(),
                Mock.Of<IDirectoryService>(),
                Mock.Of<IMageCli>(),
                Mock.Of<IManifestSigner>(),
                Mock.Of<ILogger<ISignatureProvider>>());
        }

        public void Dispose()
        {
            _directoryService.Dispose();
        }

        [Fact]
        public void Constructor_WhenKeyVaultServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSignatureProvider(
                    keyVaultService: null!,
                    Mock.Of<IContainerProvider>(),
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IDirectoryService>(),
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<ILogger<ISignatureProvider>>()));

            Assert.Equal("keyVaultService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenContainerProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSignatureProvider(
                    Mock.Of<IKeyVaultService>(),
                    containerProvider: null!,
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IDirectoryService>(),
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<ILogger<ISignatureProvider>>()));

            Assert.Equal("containerProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSignatureProvider(
                    Mock.Of<IKeyVaultService>(),
                    Mock.Of<IContainerProvider>(),
                    serviceProvider: null!,
                    Mock.Of<IDirectoryService>(),
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<ILogger<ISignatureProvider>>()));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenDirectoryServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSignatureProvider(
                    Mock.Of<IKeyVaultService>(),
                    Mock.Of<IContainerProvider>(),
                    Mock.Of<IServiceProvider>(),
                    directoryService: null!,
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<ILogger<ISignatureProvider>>()));

            Assert.Equal("directoryService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenMageCliIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSignatureProvider(
                    Mock.Of<IKeyVaultService>(),
                    Mock.Of<IContainerProvider>(),
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IDirectoryService>(),
                    mageCli: null!,
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<ILogger<ISignatureProvider>>()));

            Assert.Equal("mageCli", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenManifestSignerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSignatureProvider(
                    Mock.Of<IKeyVaultService>(),
                    Mock.Of<IContainerProvider>(),
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IDirectoryService>(),
                    Mock.Of<IMageCli>(),
                    manifestSigner: null!,
                    Mock.Of<ILogger<ISignatureProvider>>()));

            Assert.Equal("manifestSigner", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSignatureProvider(
                    Mock.Of<IKeyVaultService>(),
                    Mock.Of<IContainerProvider>(),
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IDirectoryService>(),
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public void CanSign_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _provider.CanSign(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Theory]
        [InlineData(".clickonce")]
        [InlineData(".CLICKONCE")] // test case insensitivity
        public void CanSign_WhenFileExtensionMatches_ReturnsTrue(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.True(_provider.CanSign(file));
        }

        [Theory]
        [InlineData(".txt")]
        [InlineData(".clİckonce")] // Turkish İ (U+0130)
        [InlineData(".clıckonce")] // Turkish ı (U+0131)
        public void CanSign_WhenFileExtensionDoesNotMatch_ReturnsFalse(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.False(_provider.CanSign(file));
        }

        [Fact]
        public async Task SignAsync_WhenFilesIsNull_Throws()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => _provider.SignAsync(
                    files: null!,
                    new SignOptions(HashAlgorithmName.SHA256, new Uri("http://timestamp.test"))));

            Assert.Equal("files", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenOptionsIsNull_Throws()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => _provider.SignAsync(
                    Enumerable.Empty<FileInfo>(),
                    options: null!));

            Assert.Equal("options", exception.ParamName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("PublisherName")]
        public async Task SignAsync_WhenFilesIsClickOnceFile_Signs(string publisherName)
        {
            const string commonName = "Test certificate (DO NOT TRUST)";

            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                FileInfo clickOnceFile = new(
                    Path.Combine(
                        temporaryDirectory.Directory.FullName,
                        $"{Path.GetRandomFileName()}.clickonce"));

                ContainerSpy containerSpy = new(clickOnceFile);

                FileInfo applicationFile = AddFile(
                    containerSpy,
                    temporaryDirectory.Directory,
                    string.Empty,
                    "MyApp.application");
                FileInfo dllDeployFile = AddFile(
                    containerSpy,
                    temporaryDirectory.Directory,
                    string.Empty,
                    "MyApp_1_0_0_0", "MyApp.dll.deploy");
                // This is an incomplete manifest --- just enough to satisfy SignAsync(...)'s requirements.
                FileInfo manifestFile = AddFile(
                    containerSpy,
                    temporaryDirectory.Directory,
                    @$"<?xml version=""1.0"" encoding=""utf-8""?>
<asmv1:assembly xsi:schemaLocation=""urn:schemas-microsoft-com:asm.v1 assembly.adaptive.xsd"" manifestVersion=""1.0"" xmlns:asmv1=""urn:schemas-microsoft-com:asm.v1"" xmlns=""urn:schemas-microsoft-com:asm.v2"" xmlns:asmv2=""urn:schemas-microsoft-com:asm.v2"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:co.v1=""urn:schemas-microsoft-com:clickonce.v1"" xmlns:asmv3=""urn:schemas-microsoft-com:asm.v3"" xmlns:dsig=""http://www.w3.org/2000/09/xmldsig#"" xmlns:co.v2=""urn:schemas-microsoft-com:clickonce.v2"">
  <publisherIdentity name=""CN={commonName}, O=unit.test"" />
</asmv1:assembly>",
                    "MyApp_1_0_0_0", "MyApp.dll.manifest");
                FileInfo exeDeployFile = AddFile(
                    containerSpy,
                    temporaryDirectory.Directory,
                    string.Empty,
                    "MyApp_1_0_0_0", "MyApp.exe.deploy");
                FileInfo jsonDeployFile = AddFile(
                    containerSpy,
                    temporaryDirectory.Directory,
                    string.Empty,
                    "MyApp_1_0_0_0", "MyApp.json.deploy");

                SignOptions options = new(
                    "ApplicationName",
                    publisherName,
                    "Description",
                    new Uri("https://description.test"),
                    HashAlgorithmName.SHA256,
                    HashAlgorithmName.SHA256,
                    new Uri("http://timestamp.test"),
                    matcher: null,
                    antiMatcher: null);

                using (X509Certificate2 certificate = CreateCertificate())
                using (RSA privateKey = certificate.GetRSAPrivateKey()!)
                {
                    Mock<IKeyVaultService> keyVaultService = new();

                    keyVaultService.Setup(x => x.GetCertificateAsync())
                        .ReturnsAsync(certificate);

                    keyVaultService.Setup(x => x.GetRsaAsync())
                        .ReturnsAsync(privateKey);

                    Mock<IContainerProvider> containerProvider = new();

                    containerProvider.Setup(x => x.GetContainer(It.IsAny<FileInfo>()))
                        .Returns(containerSpy);

                    Mock<IServiceProvider> serviceProvider = new();
                    AggregatingSignatureProviderSpy aggregatingSignatureProviderSpy = new();

                    serviceProvider.Setup(x => x.GetService(It.IsAny<Type>()))
                        .Returns(aggregatingSignatureProviderSpy);

                    IDirectoryService directoryService = Mock.Of<IDirectoryService>();
                    Mock<IMageCli> mageCli = new();
                    string expectedArgs = $"-update \"{manifestFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\"";
                    mageCli.Setup(x => x.RunAsync(
                            It.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))))
                        .ReturnsAsync(0);

                    string publisher;

                    if (string.IsNullOrEmpty(options.PublisherName))
                    {
                        publisher = commonName;
                    }
                    else
                    {
                        publisher = options.PublisherName;
                    }

                    expectedArgs = $"-update \"{applicationFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\" -appm \"{manifestFile.FullName}\" -pub \"{publisher}\"  -SupportURL https://description.test/";
                    mageCli.Setup(x => x.RunAsync(
                            It.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))))
                        .ReturnsAsync(0);

                    Mock<IManifestSigner> manifestSigner = new();

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => ReferenceEquals(manifestFile, fi)),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => ReferenceEquals(applicationFile, fi)),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    ILogger<ISignatureProvider> logger = Mock.Of<ILogger<ISignatureProvider>>();
                    ClickOnceSignatureProvider provider = new(
                        keyVaultService.Object,
                        containerProvider.Object,
                        serviceProvider.Object,
                        directoryService,
                        mageCli.Object,
                        manifestSigner.Object,
                        logger);

                    await provider.SignAsync(new[] { clickOnceFile }, options);

                    Assert.Equal(1, containerSpy.OpenAsync_CallCount);
                    Assert.Equal(0, containerSpy.GetFilesWithMatcher_CallCount);
                    Assert.Equal(2, containerSpy.GetFiles_CallCount);
                    Assert.Equal(1, containerSpy.SaveAsync_CallCount);
                    Assert.Equal(1, containerSpy.Dispose_CallCount);

                    // Verify that files have been renamed back.
                    foreach (FileInfo file in containerSpy.Files)
                    {
                        file.Refresh();

                        Assert.True(file.Exists);
                    }

                    Assert.Equal(3, aggregatingSignatureProviderSpy.FilesSubmittedForSigning.Count);
                    Assert.Collection(
                        aggregatingSignatureProviderSpy.FilesSubmittedForSigning,
                        file => Assert.Equal(
                            Path.Combine(dllDeployFile.DirectoryName!, Path.GetFileNameWithoutExtension(dllDeployFile.Name)),
                            file.FullName),
                        file => Assert.Equal(
                            Path.Combine(exeDeployFile.DirectoryName!, Path.GetFileNameWithoutExtension(exeDeployFile.Name)),
                            file.FullName),
                        file => Assert.Equal(
                            Path.Combine(jsonDeployFile.DirectoryName!, Path.GetFileNameWithoutExtension(jsonDeployFile.Name)),
                            file.FullName));

                    mageCli.VerifyAll();
                    manifestSigner.VerifyAll();
                }
            }
        }

        private static FileInfo AddFile(
            ContainerSpy containerSpy,
            DirectoryInfo directory,
            string fileContent,
            params string[] fileParts)
        {
            string[] parts = new[] { directory.FullName }.Concat(fileParts).ToArray();
            FileInfo file = new(Path.Combine(parts));

            // The file needs to exist because it will be renamed.
            file.Directory!.Create();
            File.WriteAllText(file.FullName, fileContent);

            containerSpy.Files.Add(file);

            return file;
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
    }
}