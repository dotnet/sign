// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    public sealed class ClickOnceSignerTests : IDisposable
    {
        private readonly DirectoryService _directoryService = new(Mock.Of<ILogger<IDirectoryService>>());
        private readonly ClickOnceSigner _signer;

        private static readonly XmlDocumentLoader _xmlDocumentReader = new();

        private const string DeploymentManifestValidContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
            <asmv1:assembly xsi:schemaLocation=""urn:schemas-microsoft-com:asm.v1 assembly.adaptive.xsd"" manifestVersion=""1.0"" xmlns:asmv1=""urn:schemas-microsoft-com:asm.v1"" xmlns=""urn:schemas-microsoft-com:asm.v2"" xmlns:asmv2=""urn:schemas-microsoft-com:asm.v2"" xmlns:xrml=""urn:mpeg:mpeg21:2003:01-REL-R-NS"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:asmv3=""urn:schemas-microsoft-com:asm.v3"" xmlns:dsig=""http://www.w3.org/2000/09/xmldsig#"" xmlns:co.v1=""urn:schemas-microsoft-com:clickonce.v1"" xmlns:co.v2=""urn:schemas-microsoft-com:clickonce.v2"">
                <assemblyIdentity name=""MyApp.application"" version=""1.0.0.0"" publicKeyToken=""0000000000000000"" language=""neutral"" processorArchitecture=""msil"" xmlns=""urn:schemas-microsoft-com:asm.v1"" />
                <description asmv2:publisher=""Contoso Limited"" asmv2:product=""MyApp"" xmlns=""urn:schemas-microsoft-com:asm.v1"" />
                <deployment install=""false"" />
                <compatibleFrameworks xmlns=""urn:schemas-microsoft-com:clickonce.v2"">
                    <framework targetVersion=""4.8"" profile=""Full"" supportedRuntime=""4.0.30319"" />
                </compatibleFrameworks>
                <dependency>
                    <dependentAssembly dependencyType=""install"" codebase=""MyApp_1_0_0_0\MyApp.dll.manifest"" size=""14853"">
                        <assemblyIdentity name=""MyApp.dll"" version=""1.0.0.0"" publicKeyToken=""0000000000000000"" language=""neutral"" processorArchitecture=""msil"" type=""win32"" />
                    </dependentAssembly>
                </dependency>
            </asmv1:assembly>";

        private const string DeploymentManifestCodeBaseTemplate = @"<?xml version=""1.0"" encoding=""utf-8""?>
            <asmv1:assembly xsi:schemaLocation=""urn:schemas-microsoft-com:asm.v1 assembly.adaptive.xsd"" manifestVersion=""1.0"" xmlns:asmv1=""urn:schemas-microsoft-com:asm.v1"" xmlns=""urn:schemas-microsoft-com:asm.v2"" xmlns:asmv2=""urn:schemas-microsoft-com:asm.v2"" xmlns:xrml=""urn:mpeg:mpeg21:2003:01-REL-R-NS"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:asmv3=""urn:schemas-microsoft-com:asm.v3"" xmlns:dsig=""http://www.w3.org/2000/09/xmldsig#"" xmlns:co.v1=""urn:schemas-microsoft-com:clickonce.v1"" xmlns:co.v2=""urn:schemas-microsoft-com:clickonce.v2"">
                <assemblyIdentity name=""MyApp.application"" version=""1.0.0.0"" publicKeyToken=""0000000000000000"" language=""neutral"" processorArchitecture=""msil"" xmlns=""urn:schemas-microsoft-com:asm.v1"" />
                <description asmv2:publisher=""Contoso Limited"" asmv2:product=""MyApp"" xmlns=""urn:schemas-microsoft-com:asm.v1"" />
                <deployment install=""false"" />
                <compatibleFrameworks xmlns=""urn:schemas-microsoft-com:clickonce.v2"">
                    <framework targetVersion=""4.8"" profile=""Full"" supportedRuntime=""4.0.30319"" />
                </compatibleFrameworks>
                <dependency>
                    <dependentAssembly dependencyType=""install"" codebase=""{0}"" size=""14853"">
                        <assemblyIdentity name=""MyApp.dll"" version=""1.0.0.0"" publicKeyToken=""0000000000000000"" language=""neutral"" processorArchitecture=""msil"" type=""win32"" />
                    </dependentAssembly>
                </dependency>
            </asmv1:assembly>";

        public ClickOnceSignerTests()
        {
            _signer = new ClickOnceSigner(
                Mock.Of<ISignatureAlgorithmProvider>(),
                Mock.Of<ICertificateProvider>(),
                Mock.Of<IServiceProvider>(),
                Mock.Of<IMageCli>(),
                Mock.Of<IManifestSigner>(),
                Mock.Of<ILogger<IDataFormatSigner>>(),
                Mock.Of<IFileMatcher>(),
                _xmlDocumentReader);
        }

        public void Dispose()
        {
            _directoryService.Dispose();
        }

        [Fact]
        public void Constructor_WhenSignatureAlgorithmProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner(
                    signatureAlgorithmProvider: null!,
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<ILogger<IDataFormatSigner>>(),
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<IXmlDocumentLoader>()));

            Assert.Equal("signatureAlgorithmProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    certificateProvider: null!,
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<ILogger<IDataFormatSigner>>(),
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<IXmlDocumentLoader>()));

            Assert.Equal("certificateProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    serviceProvider: null!,
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<ILogger<IDataFormatSigner>>(),
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<IXmlDocumentLoader>()));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenMageCliIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IServiceProvider>(),
                    mageCli: null!,
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<ILogger<IDataFormatSigner>>(),
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<IXmlDocumentLoader>()));

            Assert.Equal("mageCli", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenManifestSignerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IMageCli>(),
                    manifestSigner: null!,
                    Mock.Of<ILogger<IDataFormatSigner>>(),
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<IXmlDocumentLoader>()));

            Assert.Equal("manifestSigner", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    logger: null!,
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<IXmlDocumentLoader>()));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenFileMatcherIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<ILogger<IDataFormatSigner>>(),
                    fileMatcher: null!,
                    Mock.Of<IXmlDocumentLoader>()));

            Assert.Equal("fileMatcher", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenXmlDocumentLoaderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<ILogger<IDataFormatSigner>>(),
                    Mock.Of<IFileMatcher>(),
                    xmlDocumentLoader: null!));

            Assert.Equal("xmlDocumentLoader", exception.ParamName);
        }

        [Fact]
        public void CanSign_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _signer.CanSign(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Theory]
        [InlineData(".application")]
        [InlineData(".APPLICATION")] // test case insensitivity
        [InlineData(".vsto")]
        public void CanSign_WhenFileExtensionMatches_ReturnsTrue(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.True(_signer.CanSign(file));
        }

        [Theory]
        [InlineData(".txt")]
        [InlineData(".applİcation")] // Turkish İ (U+0130)
        [InlineData(".applıcation")] // Turkish ı (U+0131)
        public void CanSign_WhenFileExtensionDoesNotMatch_ReturnsFalse(string extension)
        {
            FileInfo file = new($"file{extension}");

            Assert.False(_signer.CanSign(file));
        }

        [Fact]
        public async Task SignAsync_WhenFilesIsNull_Throws()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => _signer.SignAsync(
                    files: null!,
                    new SignOptions(HashAlgorithmName.SHA256, new Uri("http://timestamp.test"))));

            Assert.Equal("files", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenOptionsIsNull_Throws()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => _signer.SignAsync(
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
                    DeploymentManifestValidContent,
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
                    Mock<ISignatureAlgorithmProvider> signatureAlgorithmProvider = new();
                    Mock<ICertificateProvider> certificateProvider = new();

                    certificateProvider.Setup(x => x.GetCertificateAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(certificate);

                    signatureAlgorithmProvider.Setup(x => x.GetRsaAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(privateKey);

                    Mock<IServiceProvider> serviceProvider = new();
                    AggregatingSignerSpy aggregatingSignerSpy = new();

                    serviceProvider.Setup(x => x.GetService(It.IsAny<Type>()))
                        .Returns(aggregatingSignerSpy);

                    Mock<IMageCli> mageCli = new();
                    string expectedArgs = $"-update \"{manifestFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\"";
                    mageCli.Setup(x => x.RunAsync(
                            It.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))))
                        .ReturnsAsync(0);

                    string publisher;

                    if (string.IsNullOrEmpty(options.PublisherName))
                    {
                        publisher = certificate.SubjectName.Name;
                    }
                    else
                    {
                        publisher = options.PublisherName;
                    }

                    expectedArgs = $"-update \"{applicationFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\" -pub \"{publisher}\" -appm \"{manifestFile.FullName}\" -SupportURL https://description.test/";
                    mageCli.Setup(x => x.RunAsync(
                            It.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))))
                        .ReturnsAsync(0);

                    Mock<IManifestSigner> manifestSigner = new();
                    Mock<IFileMatcher> fileMatcher = new();

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == manifestFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == applicationFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();
                    ClickOnceSigner signer = new(
                        signatureAlgorithmProvider.Object,
                        certificateProvider.Object,
                        serviceProvider.Object,
                        mageCli.Object,
                        manifestSigner.Object,
                        logger,
                        fileMatcher.Object,
                        _xmlDocumentReader);

                    await signer.SignAsync(new[] { applicationFile }, options);

                    // Verify that files have been renamed back.
                    foreach (FileInfo file in containerSpy.Files)
                    {
                        file.Refresh();

                        Assert.True(file.Exists);
                    }

                    Assert.Equal(3, aggregatingSignerSpy.FilesSubmittedForSigning.Count);
                    Assert.Collection(
                        aggregatingSignerSpy.FilesSubmittedForSigning,
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

        [Fact]
        public async Task SignAsync_WhenFilesIsClickOnceFileWithoutContent_Signs()
        {
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
                    DeploymentManifestValidContent,
                    "MyApp.application");

                SignOptions options = new(
                    "ApplicationName",
                    "PublisherName",
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
                    Mock<ISignatureAlgorithmProvider> signatureAlgorithmProvider = new();
                    Mock<ICertificateProvider> certificateProvider = new();

                    certificateProvider.Setup(x => x.GetCertificateAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(certificate);

                    signatureAlgorithmProvider.Setup(x => x.GetRsaAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(privateKey);

                    Mock<IServiceProvider> serviceProvider = new();
                    AggregatingSignerSpy aggregatingSignerSpy = new();

                    serviceProvider.Setup(x => x.GetService(It.IsAny<Type>()))
                        .Returns(aggregatingSignerSpy);

                    Mock<IMageCli> mageCli = new();

                    string publisher;

                    if (string.IsNullOrEmpty(options.PublisherName))
                    {
                        publisher = certificate.SubjectName.Name;
                    }
                    else
                    {
                        publisher = options.PublisherName;
                    }

                    string expectedArgs = $"-update \"{applicationFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\" -pub \"{publisher}\" -SupportURL https://description.test/";
                    mageCli.Setup(x => x.RunAsync(
                            It.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))))
                        .ReturnsAsync(0);

                    Mock<IManifestSigner> manifestSigner = new();
                    Mock<IFileMatcher> fileMatcher = new();

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == applicationFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();
                    ClickOnceSigner signer = new(
                        signatureAlgorithmProvider.Object,
                        certificateProvider.Object,
                        serviceProvider.Object,
                        mageCli.Object,
                        manifestSigner.Object,
                        logger,
                        fileMatcher.Object,
                        _xmlDocumentReader);

                    await signer.SignAsync(new[] { applicationFile }, options);

                    // Verify that files have been renamed back.
                    foreach (FileInfo file in containerSpy.Files)
                    {
                        file.Refresh();

                        Assert.True(file.Exists);
                    }

                    Assert.Empty(aggregatingSignerSpy.FilesSubmittedForSigning);

                    mageCli.VerifyAll();
                    manifestSigner.VerifyAll();
                }
            }
        }

        [Fact]
        public void CopySigningDependencies_CopiesCorrectFiles()
        {
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

                using (X509Certificate2 certificate = CreateCertificate())
                using (RSA privateKey = certificate.GetRSAPrivateKey()!)
                {
                    Mock<ISignatureAlgorithmProvider> signatureAlgorithmProvider = new();
                    Mock<ICertificateProvider> certificateProvider = new();

                    certificateProvider.Setup(x => x.GetCertificateAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(certificate);

                    signatureAlgorithmProvider.Setup(x => x.GetRsaAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(privateKey);

                    Mock<IServiceProvider> serviceProvider = new();
                    AggregatingSignerSpy aggregatingSignerSpy = new();

                    serviceProvider.Setup(x => x.GetService(It.IsAny<Type>()))
                        .Returns(aggregatingSignerSpy);

                    Mock<IMageCli> mageCli = new();
                    string publisher = certificate.SubjectName.Name;

                    Mock<IManifestSigner> manifestSigner = new();
                    Mock<IFileMatcher> fileMatcher = new();

                    SignOptions options = new(
                        "ApplicationName",
                        "PublisherName",
                        "Description",
                        new Uri("https://description.test"),
                        HashAlgorithmName.SHA256,
                        HashAlgorithmName.SHA256,
                        new Uri("http://timestamp.test"),
                        matcher: null,
                        antiMatcher: null
                    );

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == applicationFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();
                    ClickOnceSigner signer = new(
                        signatureAlgorithmProvider.Object,
                        certificateProvider.Object,
                        serviceProvider.Object,
                        mageCli.Object,
                        manifestSigner.Object,
                        logger,
                        fileMatcher.Object,
                        _xmlDocumentReader);

                    using (TemporaryDirectory signingDirectory = new(_directoryService))
                    {
                        // ensure that we start with nothing
                        Assert.Empty(signingDirectory.Directory.EnumerateFiles());
                        Assert.Empty(signingDirectory.Directory.EnumerateDirectories());
                        // tell the provider to copy what it needs into the signing directory
                        signer.CopySigningDependencies(applicationFile, signingDirectory.Directory, options);
                        // and make sure we got it. We expect only the DLL to be copied, and NOT the .application file itself.
                        IEnumerable<FileInfo> copiedFiles = signingDirectory.Directory.EnumerateFiles("*", SearchOption.AllDirectories);
                        IEnumerable<DirectoryInfo> copiedDirectories = signingDirectory.Directory.EnumerateDirectories();
                        Assert.Single(copiedFiles);
                        Assert.Single(copiedDirectories);
                        Assert.Contains(copiedDirectories, d => d.Name == "MyApp_1_0_0_0");
                        Assert.Contains(copiedFiles, f => f.Name == "MyApp.dll.deploy");
                    }
                }
            }
        }

        [Fact]
        public async Task SignAsync_WhenFilesIsClickOnce_DetectsCorrectManifest()
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
                    DeploymentManifestValidContent,
                    "MyApp.application");
                // This is an incomplete manifest --- just enough to satisfy SignAsync(...)'s requirements.
                FileInfo manifestFile = AddFile(
                    containerSpy,
                    temporaryDirectory.Directory,
                    @$"<?xml version=""1.0"" encoding=""utf-8""?>
<asmv1:assembly xsi:schemaLocation=""urn:schemas-microsoft-com:asm.v1 assembly.adaptive.xsd"" manifestVersion=""1.0"" xmlns:asmv1=""urn:schemas-microsoft-com:asm.v1"" xmlns=""urn:schemas-microsoft-com:asm.v2"" xmlns:asmv2=""urn:schemas-microsoft-com:asm.v2"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:co.v1=""urn:schemas-microsoft-com:clickonce.v1"" xmlns:asmv3=""urn:schemas-microsoft-com:asm.v3"" xmlns:dsig=""http://www.w3.org/2000/09/xmldsig#"" xmlns:co.v2=""urn:schemas-microsoft-com:clickonce.v2"">
  <publisherIdentity name=""CN={commonName}, O=unit.test"" />
</asmv1:assembly>",
                    "MyApp_1_0_0_0", "MyApp.dll.manifest");
                // A second, unrelated manifest file, which we want to ignore and not sign.
                FileInfo secondManifestFile = AddFile(
                    containerSpy,
                    temporaryDirectory.Directory,
                    @$"<?xml version=""1.0"" encoding=""utf-8""?>
<asmv1:assembly xsi:schemaLocation=""urn:schemas-microsoft-com:asm.v1 assembly.adaptive.xsd"" manifestVersion=""1.0"" xmlns:asmv1=""urn:schemas-microsoft-com:asm.v1"" xmlns=""urn:schemas-microsoft-com:asm.v2"" xmlns:asmv2=""urn:schemas-microsoft-com:asm.v2"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:co.v1=""urn:schemas-microsoft-com:clickonce.v1"" xmlns:asmv3=""urn:schemas-microsoft-com:asm.v3"" xmlns:dsig=""http://www.w3.org/2000/09/xmldsig#"" xmlns:co.v2=""urn:schemas-microsoft-com:clickonce.v2"">
  <publisherIdentity name=""CN={commonName}, O=unit.test"" />
</asmv1:assembly>",
                    "MyApp_1_0_0_0", "Some.Dependency.dll.manifest");

                SignOptions options = new(
                    "ApplicationName",
                    "PublisherName",
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
                    Mock<ISignatureAlgorithmProvider> signatureAlgorithmProvider = new();
                    Mock<ICertificateProvider> certificateProvider = new();

                    certificateProvider.Setup(x => x.GetCertificateAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(certificate);

                    signatureAlgorithmProvider.Setup(x => x.GetRsaAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(privateKey);

                    Mock<IServiceProvider> serviceProvider = new();
                    AggregatingSignerSpy aggregatingSignerSpy = new();

                    serviceProvider.Setup(x => x.GetService(It.IsAny<Type>()))
                        .Returns(aggregatingSignerSpy);

                    Mock<IMageCli> mageCli = new();
                    string expectedArgs = $"-update \"{manifestFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\"";
                    mageCli.Setup(x => x.RunAsync(
                            It.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))))
                        .ReturnsAsync(0);

                    string publisher;

                    if (string.IsNullOrEmpty(options.PublisherName))
                    {
                        publisher = certificate.SubjectName.Name;
                    }
                    else
                    {
                        publisher = options.PublisherName;
                    }

                    expectedArgs = $"-update \"{applicationFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\" -pub \"{publisher}\" -appm \"{manifestFile.FullName}\" -SupportURL https://description.test/";
                    mageCli.Setup(x => x.RunAsync(
                            It.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))))
                        .ReturnsAsync(0);

                    Mock<IManifestSigner> manifestSigner = new();
                    Mock<IFileMatcher> fileMatcher = new();

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == manifestFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == applicationFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();
                    ClickOnceSigner signer = new(
                        signatureAlgorithmProvider.Object,
                        certificateProvider.Object,
                        serviceProvider.Object,
                        mageCli.Object,
                        manifestSigner.Object,
                        logger,
                        fileMatcher.Object,
                        _xmlDocumentReader);

                    await signer.SignAsync(new[] { applicationFile }, options);

                    // None of the files were .deploy files and so none would have been renamed.
                    // Still, make sure they exist.
                    foreach (FileInfo file in containerSpy.Files)
                    {
                        file.Refresh();

                        Assert.True(file.Exists);
                    }

                    mageCli.VerifyAll();
                    manifestSigner.VerifyAll();

                    // make sure we never tried to sign the second manifest file
                    manifestSigner.Verify(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == secondManifestFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))), Times.Never());
                }
            }
        }

        [Fact]
        public void TryGetApplicationManifestFileName_WhenCodeBaseAttributeIsMissing_ReturnsFalse()
        {
            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                string xml = ModifyXml(
                    DeploymentManifestCodeBaseTemplate,
                    (XmlDocument xmlDoc, XmlNamespaceManager xmlNamespaceManager) =>
                    {
                        XmlNodeList nodes = xmlDoc.SelectNodes(
                            "//asmv1:assembly/asmv2:dependency/asmv2:dependentAssembly[@codebase]",
                            xmlNamespaceManager)!;

                        foreach (XmlNode node in nodes)
                        {
                            XmlAttribute codebaseAttribute = node.Attributes!["codebase"]!;

                            node.Attributes.Remove(codebaseAttribute);
                        }
                    });
                FileInfo file = CreateFile(temporaryDirectory.Directory, xml);

                bool actualResult = _signer.TryGetApplicationManifestFileName(file, out string? applicationManifestFileName);

                Assert.False(actualResult);
            }
        }

        [Fact]
        public void TryGetApplicationManifestFileName_WhenDependentAssemblyElementIsMissing_ReturnsFalse()
        {
            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                string xml = ModifyXml(
                    DeploymentManifestCodeBaseTemplate,
                    (XmlDocument xmlDoc, XmlNamespaceManager xmlNamespaceManager) =>
                    {
                        XmlNodeList nodes = xmlDoc.SelectNodes(
                            "//asmv1:assembly/asmv2:dependency/asmv2:dependentAssembly",
                            xmlNamespaceManager)!;

                        foreach (XmlNode node in nodes)
                        {
                            node.ParentNode!.RemoveChild(node);
                        }
                    });
                FileInfo file = CreateFile(temporaryDirectory.Directory, xml);

                bool actualResult = _signer.TryGetApplicationManifestFileName(file, out string? applicationManifestFileName);

                Assert.False(actualResult);
            }
        }

        [Fact]
        public void TryGetApplicationManifestFileName_WhenMultipleDependentAssemblyElementsArePresent_ReturnsFalse()
        {
            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                string xml = ModifyXml(
                    string.Format(DeploymentManifestCodeBaseTemplate, @"MyApp_1_0_0_0\MyApp.dll.manifest"),
                    (XmlDocument xmlDoc, XmlNamespaceManager xmlNamespaceManager) =>
                    {
                        XmlNodeList nodes = xmlDoc.SelectNodes(
                            "//asmv1:assembly/asmv2:dependency/asmv2:dependentAssembly",
                            xmlNamespaceManager)!;

                        foreach (XmlNode node in nodes)
                        {
                            XmlNode duplicateNode = node.CloneNode(deep: true);

                            node.ParentNode!.AppendChild(duplicateNode);
                        }
                    });
                FileInfo file = CreateFile(temporaryDirectory.Directory, xml);

                bool actualResult = _signer.TryGetApplicationManifestFileName(file, out string? applicationManifestFileName);

                Assert.False(actualResult);
            }
        }

        [Theory]
        [InlineData(@"MyApp_1_0_0_0\MyApp.dll.manifest")]
        [InlineData("https://unit.test/MyApp.dll.manifest")]
        public void TryGetApplicationManifestFileName_WhenCodeBaseIsValid_ReturnsTrue(string codebase)
        {
            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                string xml = string.Format(DeploymentManifestCodeBaseTemplate, codebase);
                FileInfo file = CreateFile(temporaryDirectory.Directory, xml);

                bool actualResult = _signer.TryGetApplicationManifestFileName(file, out string? applicationManifestFileName);

                Assert.True(actualResult);
                Assert.Equal("MyApp.dll.manifest", applicationManifestFileName);
            }
        }

        private static string ModifyXml(string xml, Action<XmlDocument, XmlNamespaceManager> modify)
        {
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null // prevent external entity resolution
            };
            XmlDocument xmlDoc = new();

            using (StringReader stringReader = new(xml))
            using (XmlReader xmlReader = XmlReader.Create(stringReader, settings))
            {
                xmlDoc.Load(xmlReader);
            }

            XmlNamespaceManager namespaceManager = new(xmlDoc.NameTable);

            namespaceManager.AddNamespace("asmv1", "urn:schemas-microsoft-com:asm.v1");
            namespaceManager.AddNamespace("asmv2", "urn:schemas-microsoft-com:asm.v2");
            namespaceManager.AddNamespace("asmv3", "urn:schemas-microsoft-com:asm.v3");
            namespaceManager.AddNamespace("dsig", "http://www.w3.org/2000/09/xmldsig#");
            namespaceManager.AddNamespace("co.v1", "urn:schemas-microsoft-com:clickonce.v1");
            namespaceManager.AddNamespace("co.v2", "urn:schemas-microsoft-com:clickonce.v2");
            namespaceManager.AddNamespace("xrml", "urn:mpeg:mpeg21:2003:01-REL-R-NS");
            namespaceManager.AddNamespace("xsi", "http://www.w3.org/2001/XMLSchema-instance");

            modify(xmlDoc, namespaceManager);

            using (StringWriter stringWriter = new())
            using (XmlWriter xmlWriter = XmlWriter.Create(stringWriter))
            {
                xmlDoc.WriteTo(xmlWriter);
                xmlWriter.Flush();

                return stringWriter.GetStringBuilder().ToString();
            }
        }

        private static FileInfo CreateFile(DirectoryInfo directory, string xml)
        {
            FileInfo file = new(Path.Combine(directory.FullName, "test.xml"));

            File.WriteAllText(file.FullName, xml);

            return file;
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
