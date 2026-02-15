// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed class ClickOnceSigner2Tests : IDisposable
    {
        private readonly DirectoryService _directoryService;
        private readonly IClickOnceAppFactory _clickOnceAppFactory;
        private readonly ClickOnceSigner2 _signer;

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

        public ClickOnceSigner2Tests()
        {
            // Initialize MSBuildLocator for ManifestReaderAdapter and TestClickOnceApp.Create() 
            // which use Microsoft.Build.Tasks.Deployment.ManifestUtilities APIs
            MsBuildLocatorHelper.EnsureInitialized();

            _directoryService = new(Mock.Of<ILogger<IDirectoryService>>());
            _clickOnceAppFactory = new ClickOnceAppFactory(new ManifestReaderAdapter());
            _signer = new ClickOnceSigner2(
                Mock.Of<ISignatureAlgorithmProvider>(),
                Mock.Of<ICertificateProvider>(),
                Mock.Of<IServiceProvider>(),
                Mock.Of<IMageCli>(),
                Mock.Of<IManifestSigner>(),
                Mock.Of<IFileMatcher>(),
                _clickOnceAppFactory,
                new ManifestReaderAdapter(),
                Mock.Of<ILogger<IDataFormatSigner>>());
        }

        public void Dispose()
        {
            _directoryService.Dispose();
        }

        [Fact]
        public void Constructor_WhenSignatureAlgorithmProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner2(
                    signatureAlgorithmProvider: null!,
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<IFileMatcher>(),
                    _clickOnceAppFactory,
                    new ManifestReaderAdapter(),
                    Mock.Of<ILogger<IDataFormatSigner>>()));

            Assert.Equal("signatureAlgorithmProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner2(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    certificateProvider: null!,
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<IFileMatcher>(),
                    _clickOnceAppFactory,
                    new ManifestReaderAdapter(),
                    Mock.Of<ILogger<IDataFormatSigner>>()));

            Assert.Equal("certificateProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner2(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    serviceProvider: null!,
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<IFileMatcher>(),
                    _clickOnceAppFactory,
                    new ManifestReaderAdapter(),
                    Mock.Of<ILogger<IDataFormatSigner>>()));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenMageCliIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner2(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IServiceProvider>(),
                    mageCli: null!,
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<IFileMatcher>(),
                    _clickOnceAppFactory,
                    new ManifestReaderAdapter(),
                    Mock.Of<ILogger<IDataFormatSigner>>()));

            Assert.Equal("mageCli", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenManifestSignerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner2(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IMageCli>(),
                    manifestSigner: null!,
                    Mock.Of<IFileMatcher>(),
                    _clickOnceAppFactory,
                    new ManifestReaderAdapter(),
                    Mock.Of<ILogger<IDataFormatSigner>>()));

            Assert.Equal("manifestSigner", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner2(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    Mock.Of<IFileMatcher>(),
                    _clickOnceAppFactory,
                    new ManifestReaderAdapter(),
                    logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenFileMatcherIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner2(
                    Mock.Of<ISignatureAlgorithmProvider>(),
                    Mock.Of<ICertificateProvider>(),
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IMageCli>(),
                    Mock.Of<IManifestSigner>(),
                    fileMatcher: null!,
                    _clickOnceAppFactory,
                    new ManifestReaderAdapter(),
                    Mock.Of<ILogger<IDataFormatSigner>>()));

            Assert.Equal("fileMatcher", exception.ParamName);
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
        public void CanSign_WhenFileIsClickOnceApplicationManifest_ReturnsTrue()
        {
            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                TestClickOnceApp testApp = TestClickOnceApp.Create(temporaryDirectory.Directory);

                Assert.True(_signer.CanSign(testApp.ApplicationManifestFile!));
            }
        }

        [Fact]
        public void CanSign_WhenFileIsSideBySideManifest_ReturnsFalse()
        {
            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                // Create a Win32 side-by-side manifest (not a ClickOnce manifest)
                string sideBySideManifestContent = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<assembly xmlns=""urn:schemas-microsoft-com:asm.v1"" manifestVersion=""1.0"">
  <assemblyIdentity
    name=""MyApp""
    version=""1.0.0.0""
    processorArchitecture=""x86""
    type=""win32""/>
  <dependency>
    <dependentAssembly>
      <assemblyIdentity
        type=""win32""
        name=""Microsoft.Windows.Common-Controls""
        version=""6.0.0.0""
        processorArchitecture=""*""
        publicKeyToken=""6595b64144ccf1df""
        language=""*""/>
    </dependentAssembly>
  </dependency>
</assembly>";

                FileInfo manifestFile = new(Path.Combine(temporaryDirectory.Directory.FullName, "app.manifest"));
                File.WriteAllText(manifestFile.FullName, sideBySideManifestContent);

                Assert.False(_signer.CanSign(manifestFile));
            }
        }

        [Fact]
        public void CanSign_WhenManifestFileDoesNotExist_ReturnsFalse()
        {
            FileInfo file = new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nonexistent.manifest"));

            Assert.False(_signer.CanSign(file));
        }

        [Fact]
        public void CanSign_WhenManifestFileIsMalformed_ReturnsFalse()
        {
            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                FileInfo manifestFile = new(Path.Combine(temporaryDirectory.Directory.FullName, "malformed.manifest"));
                File.WriteAllText(manifestFile.FullName, "This is not valid XML");

                Assert.False(_signer.CanSign(manifestFile));
            }
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
                () => _signer.SignAsync([], options: null!));

            Assert.Equal("options", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenSigningFails_Throws()
        {
            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                TestClickOnceApp testApp = TestClickOnceApp.Create(temporaryDirectory.Directory);
                SignOptions options = new(
                    testApp.Name,
                    testApp.Publisher,
                    testApp.Description,
                    new Uri("https://description.test"),
                    HashAlgorithmName.SHA256,
                    HashAlgorithmName.SHA256,
                    new Uri("http://timestamp.test"),
                    matcher: null,
                    antiMatcher: null,
                    recurseContainers: false,
                    noSignClickOnceDeps: false,
                    noUpdateClickOnceManifest: false,
                    signedFileTracker: new SignedFileTracker());

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
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

                    mageCli.Setup(x => x.RunAsync(
                            It.IsAny<string>()))
                        .ReturnsAsync(1);

                    Mock<IManifestSigner> manifestSigner = new();
                    Mock<IFileMatcher> fileMatcher = new();
                    Mock<ILogger<IDataFormatSigner>> logger = new();

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.DeploymentManifestFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    ClickOnceSigner2 signer = new(
                        signatureAlgorithmProvider.Object,
                        certificateProvider.Object,
                        serviceProvider.Object,
                        mageCli.Object,
                        manifestSigner.Object,
                        fileMatcher.Object,
                        _clickOnceAppFactory,
                        new ManifestReaderAdapter(),
                        logger.Object);

                    signer.Retry = TimeSpan.FromMicroseconds(1);

                    await Assert.ThrowsAsync<SigningException>(
                        () => signer.SignAsync([testApp.DeploymentManifestFile], options));
                }
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("PublisherName")]
        public async Task SignAsync_WhenFilesIsClickOnceFile_Signs(string? publisherName)
        {
            const string commonName = "Test certificate (DO NOT TRUST)";
            FileInfo? dllDeployFile = null;
            FileInfo? manifestFile = null;
            FileInfo? jsonFile = null;

            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                TestClickOnceApp testApp = TestClickOnceApp.Create(temporaryDirectory.Directory,
                    additionalFilesCreator: applicationDirectory =>
                    {
                        dllDeployFile = new(Path.Combine(applicationDirectory.FullName, "App.dll"));

                        TestClickOnceApp.CreateAssembly("class A { }", Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, dllDeployFile);

                        manifestFile = new(Path.Combine(applicationDirectory.FullName, "App.dll.manifest"));

                        // This is an incomplete manifest --- just enough to satisfy SignAsync(...)'s requirements.
                        File.WriteAllText(manifestFile.FullName, @$"<?xml version=""1.0"" encoding=""utf-8""?>
<asmv1:assembly xsi:schemaLocation=""urn:schemas-microsoft-com:asm.v1 assembly.adaptive.xsd"" manifestVersion=""1.0"" xmlns:asmv1=""urn:schemas-microsoft-com:asm.v1"" xmlns=""urn:schemas-microsoft-com:asm.v2"" xmlns:asmv2=""urn:schemas-microsoft-com:asm.v2"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:co.v1=""urn:schemas-microsoft-com:clickonce.v1"" xmlns:asmv3=""urn:schemas-microsoft-com:asm.v3"" xmlns:dsig=""http://www.w3.org/2000/09/xmldsig#"" xmlns:co.v2=""urn:schemas-microsoft-com:clickonce.v2"">
  <publisherIdentity name=""CN={commonName}, O=unit.test"" />
</asmv1:assembly>");
                        jsonFile = new(Path.Combine(applicationDirectory.FullName, "App.json"));

                        File.WriteAllText(jsonFile.FullName, "{}");
                    });

                SignOptions options = new(
                    testApp.Name,
                    publisherName,
                    testApp.Description,
                    new Uri("https://description.test"),
                    HashAlgorithmName.SHA256,
                    HashAlgorithmName.SHA256,
                    new Uri("http://timestamp.test"),
                    matcher: null,
                    antiMatcher: null,
                    recurseContainers: false,
                    noSignClickOnceDeps: false,
                    noUpdateClickOnceManifest: false,
                    signedFileTracker: new SignedFileTracker());

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
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
                    string expectedArgs = $"-update \"{testApp.ApplicationManifestFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\"";
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

                    expectedArgs = $"-update \"{testApp.DeploymentManifestFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\" -pub \"{publisher}\" -appm \"{testApp.ApplicationManifestFile.FullName}\" -SupportURL https://description.test/";
                    mageCli.Setup(x => x.RunAsync(
                            It.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))))
                        .ReturnsAsync(0);

                    Mock<IManifestSigner> manifestSigner = new();
                    Mock<IFileMatcher> fileMatcher = new();

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.DeploymentManifestFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.ApplicationManifestFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();
                    ClickOnceSigner2 signer = new(
                        signatureAlgorithmProvider.Object,
                        certificateProvider.Object,
                        serviceProvider.Object,
                        mageCli.Object,
                        manifestSigner.Object,
                        fileMatcher.Object,
                        _clickOnceAppFactory,
                        new ManifestReaderAdapter(),
                        logger);

                    await signer.SignAsync([testApp.DeploymentManifestFile], options);

                    //// Verify that files have been renamed back.
                    //foreach (FileInfo file in containerSpy.Files)
                    //{
                    //    file.Refresh();

                    //    Assert.True(file.Exists);
                    //}

                    string[] expectedFiles = new[]
                    {
                        Path.Combine(testApp.ApplicationManifestFile.DirectoryName!, dllDeployFile!.Name),
                        Path.Combine(testApp.ApplicationManifestFile.DirectoryName!, "App.exe"),
                        Path.Combine(testApp.ApplicationManifestFile.DirectoryName!, manifestFile!.Name),
                        Path.Combine(testApp.ApplicationManifestFile.DirectoryName!, jsonFile!.Name)
                    };

                    Assert.Equal(
                        expectedFiles.OrderBy(f => f, StringComparer.Ordinal).ToArray(),
                        aggregatingSignerSpy.FilesSubmittedForSigning
                            .Select(f => f.FullName)
                            .OrderBy(f => f, StringComparer.Ordinal)
                            .ToArray());

                    mageCli.VerifyAll();
                    manifestSigner.VerifyAll();
                }
            }
        }

        [Fact]
        public async Task SignAsync_WhenApplicationManifestMissing_Throws()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);

            TestClickOnceApp testApp = TestClickOnceApp.Create(temporaryDirectory.Directory);

            File.Delete(testApp.ApplicationManifestFile.FullName);

            SignOptions options = new(
                testApp.Name,
                publisherName: null,
                description: testApp.Description,
                descriptionUrl: null,
                fileHashAlgorithm: HashAlgorithmName.SHA256,
                timestampHashAlgorithm: HashAlgorithmName.SHA256,
                timestampService: new Uri("http://timestamp.test"),
                matcher: null,
                antiMatcher: null,
                recurseContainers: false,
                noSignClickOnceDeps: false,
                noUpdateClickOnceManifest: false,
                signedFileTracker: new SignedFileTracker());

            using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
            using (RSA privateKey = certificate.GetRSAPrivateKey()!)
            {
                Mock<ISignatureAlgorithmProvider> signatureAlgorithmProvider = new();
                Mock<ICertificateProvider> certificateProvider = new();
                Mock<IServiceProvider> serviceProvider = new();

                serviceProvider.Setup(x => x.GetService(It.IsAny<Type>()))
                    .Returns(Mock.Of<IAggregatingDataFormatSigner>());

                Mock<IMageCli> mageCli = new();
                Mock<IManifestSigner> manifestSigner = new();
                Mock<IFileMatcher> fileMatcher = new();
                ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();

                certificateProvider.Setup(x => x.GetCertificateAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(certificate);

                signatureAlgorithmProvider.Setup(x => x.GetRsaAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(privateKey);

                ClickOnceSigner2 signer = new(
                    signatureAlgorithmProvider.Object,
                    certificateProvider.Object,
                    serviceProvider.Object,
                    mageCli.Object,
                    manifestSigner.Object,
                        fileMatcher.Object,
                        _clickOnceAppFactory,
                        new ManifestReaderAdapter(),
                        logger);

                SigningException exception = await Assert.ThrowsAsync<SigningException>(
                    () => signer.SignAsync([testApp.DeploymentManifestFile], options));

                Assert.Contains("App.exe.manifest", exception.Message);
                Assert.Contains("--no-update-clickonce-manifest", exception.Message);
            }
        }

        [Fact]
        public async Task SignAsync_WhenFilesIsClickOnceFileWithoutContent_Throws()
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
                    "AppDescription",
                    new Uri("https://description.test"),
                    HashAlgorithmName.SHA256,
                    HashAlgorithmName.SHA256,
                    new Uri("http://timestamp.test"),
                    matcher: null,
                    antiMatcher: null,
                    recurseContainers: false,
                    noSignClickOnceDeps: false,
                    noUpdateClickOnceManifest: false,
                    signedFileTracker: new SignedFileTracker());

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
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

                    string publisher;

                    if (string.IsNullOrEmpty(options.PublisherName))
                    {
                        publisher = certificate.SubjectName.Name;
                    }
                    else
                    {
                        publisher = options.PublisherName;
                    }

                    Mock<IMageCli> mageCli = new();
                    Mock<IManifestSigner> manifestSigner = new();
                    Mock<IFileMatcher> fileMatcher = new();
                    ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();

                    ClickOnceSigner2 signer = new(
                        signatureAlgorithmProvider.Object,
                        certificateProvider.Object,
                        serviceProvider.Object,
                        mageCli.Object,
                        manifestSigner.Object,
                        fileMatcher.Object,
                        _clickOnceAppFactory,
                        new ManifestReaderAdapter(),
                        logger);

                    SigningException exception = await Assert.ThrowsAsync<SigningException>(
                        () => signer.SignAsync([applicationFile], options));

                    Assert.Contains("MyApp_1_0_0_0", exception.Message);
                    Assert.Contains("--no-update-clickonce-manifest", exception.Message);
                }
            }
        }
        [Fact]
        public void CopySigningDependencies_WhenDeploymentManifestFileIsNull_Throws()
        {
            SignOptions options = new(HashAlgorithmName.SHA256, new Uri("http://timestamp.test"));

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _signer.CopySigningDependencies(
                    deploymentManifestFile: null!,
                    new DirectoryInfo(Path.GetTempPath()),
                    options));

            Assert.Equal("deploymentManifestFile", exception.ParamName);
        }

        [Fact]
        public void CopySigningDependencies_WhenDestinationIsNull_Throws()
        {
            SignOptions options = new(HashAlgorithmName.SHA256, new Uri("http://timestamp.test"));

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _signer.CopySigningDependencies(
                    new FileInfo("test.application"),
                    destination: null!,
                    options));

            Assert.Equal("destination", exception.ParamName);
        }

        [Fact]
        public void CopySigningDependencies_WhenSignOptionsIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _signer.CopySigningDependencies(
                    new FileInfo("test.application"),
                    new DirectoryInfo(Path.GetTempPath()),
                    signOptions: null!));

            Assert.Equal("signOptions", exception.ParamName);
        }

        [Fact]
        public void CopySigningDependencies_PreservesApplicationManifestSubdirectoryStructure()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);

            TestClickOnceApp testApp = TestClickOnceApp.Create(
                temporaryDirectory.Directory,
                applicationRelativeDirectoryPath: @"Application Files\App_1_0_0_0",
                mapFileExtensions: false);

            SignOptions options = new(HashAlgorithmName.SHA256, new Uri("http://timestamp.test"));

            using TemporaryDirectory signingDirectory = new(_directoryService);

            _signer.CopySigningDependencies(testApp.DeploymentManifestFile, signingDirectory.Directory, options);

            // The application manifest must be copied preserving the nested subdirectory
            // structure (e.g., Application Files\App_1_0_0_0\App.exe.manifest) so that the
            // deployment manifest's codebase attribute can resolve it correctly.
            string expectedRelativePath = Path.GetRelativePath(
                testApp.DeploymentManifestFile.DirectoryName!,
                testApp.ApplicationManifestFile.FullName);

            string expectedDestinationPath = Path.Combine(
                signingDirectory.Directory.FullName,
                expectedRelativePath);

            Assert.True(
                File.Exists(expectedDestinationPath),
                $"Application manifest should be at '{expectedRelativePath}' but was not found. " +
                $"Files found: {string.Join(", ", signingDirectory.Directory.EnumerateFiles("*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(signingDirectory.Directory.FullName, f.FullName)))}");
        }

        [Fact]
        public void CopySigningDependencies_CopiesPayloadAndManifestFiles()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);

            TestClickOnceApp testApp = TestClickOnceApp.Create(
                temporaryDirectory.Directory,
                applicationRelativeDirectoryPath: @"Application Files\App_1_0_0_0",
                mapFileExtensions: true);

            SignOptions options = new(HashAlgorithmName.SHA256, new Uri("http://timestamp.test"));

            using TemporaryDirectory signingDirectory = new(_directoryService);

            _signer.CopySigningDependencies(testApp.DeploymentManifestFile, signingDirectory.Directory, options);

            IEnumerable<FileInfo> copiedFiles = signingDirectory.Directory
                .EnumerateFiles("*", SearchOption.AllDirectories);

            // The application manifest should be copied
            Assert.Contains(copiedFiles, f => f.Name == testApp.ApplicationManifestFile.Name);

            // The payload file (App.exe.deploy) should be copied
            Assert.Contains(copiedFiles, f => f.Name == "App.exe.deploy");

            // All copied files should be under the nested subdirectory
            string nestedSubdirectory = Path.Combine("Application Files", "App_1_0_0_0");

            foreach (FileInfo copiedFile in copiedFiles)
            {
                string relativePath = Path.GetRelativePath(
                    signingDirectory.Directory.FullName,
                    copiedFile.FullName);

                Assert.StartsWith(
                    nestedSubdirectory,
                    relativePath,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void CopySigningDependencies_WhenNotDeploymentManifest_DoesNotCopyFiles()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);

            // Use a ClickOnce application manifest (not a deployment manifest) so that
            // ManifestReader.ReadManifest returns an ApplicationManifest and
            // TryReadDeployManifest returns false without throwing.
            FileInfo nonDeploymentManifestFile = new(Path.Combine(temporaryDirectory.Directory.FullName, "app.application"));
            File.WriteAllText(nonDeploymentManifestFile.FullName, @"<?xml version=""1.0"" encoding=""utf-8""?>
<asmv1:assembly xsi:schemaLocation=""urn:schemas-microsoft-com:asm.v1 assembly.adaptive.xsd"" manifestVersion=""1.0"" xmlns:asmv1=""urn:schemas-microsoft-com:asm.v1"" xmlns=""urn:schemas-microsoft-com:asm.v2"" xmlns:asmv2=""urn:schemas-microsoft-com:asm.v2"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:co.v1=""urn:schemas-microsoft-com:clickonce.v1"" xmlns:asmv3=""urn:schemas-microsoft-com:asm.v3"" xmlns:dsig=""http://www.w3.org/2000/09/xmldsig#"" xmlns:co.v2=""urn:schemas-microsoft-com:clickonce.v2"">
  <publisherIdentity name=""CN=Test, O=unit.test"" />
</asmv1:assembly>");

            SignOptions options = new(HashAlgorithmName.SHA256, new Uri("http://timestamp.test"));

            using TemporaryDirectory signingDirectory = new(_directoryService);

            _signer.CopySigningDependencies(nonDeploymentManifestFile, signingDirectory.Directory, options);

            Assert.Empty(signingDirectory.Directory.EnumerateFiles("*", SearchOption.AllDirectories));
        }

        [Fact]
        public void CopySigningDependencies_WhenMapFileExtensionsIsTrue_DoesNotLogResolveFileWarnings()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);

            TestClickOnceApp testApp = TestClickOnceApp.Create(
                temporaryDirectory.Directory,
                applicationRelativeDirectoryPath: @"Application Files\App_1_0_0_0",
                mapFileExtensions: true);

            SignOptions options = new(HashAlgorithmName.SHA256, new Uri("http://timestamp.test"));

            using TemporaryDirectory signingDirectory = new(_directoryService);

            Mock<ILogger<IDataFormatSigner>> logger = new();

            ClickOnceSigner2 signer = new(
                Mock.Of<ISignatureAlgorithmProvider>(),
                Mock.Of<ICertificateProvider>(),
                Mock.Of<IServiceProvider>(),
                Mock.Of<IMageCli>(),
                Mock.Of<IManifestSigner>(),
                Mock.Of<IFileMatcher>(),
                _clickOnceAppFactory,
                new ManifestReaderAdapter(),
                logger.Object);

            signer.CopySigningDependencies(testApp.DeploymentManifestFile, signingDirectory.Directory, options);

            // Verify no warning or error messages were logged.
            // Before the fix, ResolveFiles() would emit MSB3113 warnings because
            // payload files have .deploy suffixes but the manifest references them without.
            logger.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l >= LogLevel.Warning),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);

            // Verify .deploy files are still intact after the call (restore worked correctly).
            string applicationDirectory = testApp.ApplicationManifestFile.Directory!.FullName;
            FileInfo[] deployFiles = new DirectoryInfo(applicationDirectory)
                .GetFiles("*.deploy", SearchOption.AllDirectories);

            Assert.NotEmpty(deployFiles);
            Assert.Contains(deployFiles, f => f.Name == "App.exe.deploy");
        }

        private static MockClickOnceApp CreateMockClickOnceApp(TemporaryDirectory temporaryDirectory)
        {
            const string AppName = "MyApp";
            Version version = new(major: 1, minor: 0, build: 0, revision: 0);

            DirectoryInfo applicationFilesDirectory = temporaryDirectory.Directory.CreateSubdirectory("Application Files");
            DirectoryInfo versionDirectory = applicationFilesDirectory.CreateSubdirectory($"{AppName}_{version.Major}_{version.Minor}_{version.Build}_{version.Revision}");

            FileInfo applicationManifestFile = CreateApplicationManifest(versionDirectory, AppName, version);
            FileInfo deploymentManifestFile = CreateDeploymentManifest(temporaryDirectory.Directory, AppName, version, applicationManifestFile);

            return new MockClickOnceApp(applicationManifestFile, deploymentManifestFile);
        }

        private sealed class MockClickOnceApp
        {
            internal FileInfo ApplicationManifest { get; }
            internal FileInfo DeploymentManifest { get; }

            internal MockClickOnceApp(
                FileInfo applicationManifest,
                FileInfo deploymentManifest)
            {
                ApplicationManifest = applicationManifest;
                DeploymentManifest = deploymentManifest;
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
                // Create a proper application manifest that ManifestReader will parse as ApplicationManifest.
                DirectoryInfo appManifestDir = new(Path.Combine(temporaryDirectory.Directory.FullName, "MyApp_1_0_0_0"));
                FileInfo manifestFile = CreateApplicationManifest(appManifestDir, "MyApp", new Version(1, 0, 0, 0));
                containerSpy.Files.Add(manifestFile);
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
                    "AppDescription",
                    new Uri("https://description.test"),
                    HashAlgorithmName.SHA256,
                    HashAlgorithmName.SHA256,
                    new Uri("http://timestamp.test"),
                    matcher: null,
                    antiMatcher: null,
                    recurseContainers: false,
                    noSignClickOnceDeps: false,
                    noUpdateClickOnceManifest: false,
                    signedFileTracker: new SignedFileTracker());

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
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
                    ClickOnceSigner2 signer = new(
                        signatureAlgorithmProvider.Object,
                        certificateProvider.Object,
                        serviceProvider.Object,
                        mageCli.Object,
                        manifestSigner.Object,
                        fileMatcher.Object,
                        _clickOnceAppFactory,
                        new ManifestReaderAdapter(),
                        logger);

                    await signer.SignAsync([applicationFile], options);

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

        //[Fact]
        //public void TryGetApplicationManifestFileName_WhenCodeBaseAttributeIsMissing_ReturnsFalse()
        //{
        //    using (TemporaryDirectory temporaryDirectory = new(_directoryService))
        //    {
        //        string xml = ModifyXml(
        //            DeploymentManifestCodeBaseTemplate,
        //            (XmlDocument xmlDoc, XmlNamespaceManager xmlNamespaceManager) =>
        //            {
        //                XmlNodeList nodes = xmlDoc.SelectNodes(
        //                    "//asmv1:assembly/asmv2:dependency/asmv2:dependentAssembly[@codebase]",
        //                    xmlNamespaceManager)!;

        //                foreach (XmlNode node in nodes)
        //                {
        //                    XmlAttribute codebaseAttribute = node.Attributes!["codebase"]!;

        //                    node.Attributes.Remove(codebaseAttribute);
        //                }
        //            });
        //        FileInfo file = CreateFile(temporaryDirectory.Directory, xml);

        //        bool actualResult = _signer.TryGetApplicationManifestFileName(file, out string? applicationManifestFileName);

        //        Assert.False(actualResult);
        //    }
        //}

        //[Fact]
        //public void TryGetApplicationManifestFileName_WhenDependentAssemblyElementIsMissing_ReturnsFalse()
        //{
        //    using (TemporaryDirectory temporaryDirectory = new(_directoryService))
        //    {
        //        string xml = ModifyXml(
        //            DeploymentManifestCodeBaseTemplate,
        //            (XmlDocument xmlDoc, XmlNamespaceManager xmlNamespaceManager) =>

        [Fact]
        public async Task SignAsync_WhenPayloadRequiresFallbackSearchPath_Succeeds()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);

            TestClickOnceApp testApp = TestClickOnceApp.Create(
                temporaryDirectory.Directory,
                applicationRelativeDirectoryPath: @"Application Files\App_1_0_0_0",
                mapFileExtensions: false,
                additionalFilesCreator: applicationDirectory =>
                {
                    string payloadPath = Path.Combine(applicationDirectory.FullName, "content.dat");

                    File.WriteAllText(payloadPath, "payload");
                });

            string applicationDirectoryPath = testApp.ApplicationManifestFile.Directory!.FullName;
            string deploymentDirectoryPath = testApp.DeploymentManifestFile.Directory!.FullName;
            string payloadInApplicationDirectory = Path.Combine(applicationDirectoryPath, "content.dat");
            string payloadInDeploymentDirectory = Path.Combine(deploymentDirectoryPath, "content.dat");

            File.Move(payloadInApplicationDirectory, payloadInDeploymentDirectory, overwrite: true);

            SignOptions options = new(
                testApp.Name,
                testApp.Publisher,
                testApp.Description,
                new Uri("https://description.test"),
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256,
                new Uri("http://timestamp.test"),
                matcher: null,
                antiMatcher: null,
                recurseContainers: false,
                noSignClickOnceDeps: false,
                noUpdateClickOnceManifest: false,
                signedFileTracker: new SignedFileTracker());

            using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
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

                mageCli.Setup(x => x.RunAsync(It.IsAny<string>()))
                    .ReturnsAsync(0);

                Mock<IManifestSigner> manifestSigner = new();
                Mock<IFileMatcher> fileMatcher = new();

                ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();

                manifestSigner.Setup(
                    x => x.Sign(
                        It.Is<FileInfo>(fi => fi.Name == testApp.DeploymentManifestFile.Name),
                        It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                        It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                        It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                manifestSigner.Setup(
                    x => x.Sign(
                        It.Is<FileInfo>(fi => fi.Name == testApp.ApplicationManifestFile.Name),
                        It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                        It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                        It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                ClickOnceSigner2 signer = new(
                    signatureAlgorithmProvider.Object,
                    certificateProvider.Object,
                    serviceProvider.Object,
                    mageCli.Object,
                    manifestSigner.Object,
                        fileMatcher.Object,
                        _clickOnceAppFactory,
                        new ManifestReaderAdapter(),
                        logger);

                await signer.SignAsync([testApp.DeploymentManifestFile], options);

                Assert.NotEmpty(aggregatingSignerSpy.FilesSubmittedForSigning);
                Assert.Contains(
                    aggregatingSignerSpy.FilesSubmittedForSigning,
                    file => string.Equals(file.Name, "content.dat", StringComparison.OrdinalIgnoreCase));

                manifestSigner.VerifyAll();
            }
        }
        //            {
        //                XmlNodeList nodes = xmlDoc.SelectNodes(
        //                    "//asmv1:assembly/asmv2:dependency/asmv2:dependentAssembly",
        //                    xmlNamespaceManager)!;

        //                foreach (XmlNode node in nodes)
        //                {
        //                    node.ParentNode!.RemoveChild(node);
        //                }
        //            });
        //        FileInfo file = CreateFile(temporaryDirectory.Directory, xml);

        //        bool actualResult = _signer.TryGetApplicationManifestFileName(file, out string? applicationManifestFileName);

        //        Assert.False(actualResult);
        //    }
        //}

        //[Fact]
        //public void TryGetApplicationManifestFileName_WhenMultipleDependentAssemblyElementsArePresent_ReturnsFalse()
        //{
        //    using (TemporaryDirectory temporaryDirectory = new(_directoryService))
        //    {
        //        string xml = ModifyXml(
        //            string.Format(DeploymentManifestCodeBaseTemplate, @"MyApp_1_0_0_0\MyApp.dll.manifest"),
        //            (XmlDocument xmlDoc, XmlNamespaceManager xmlNamespaceManager) =>
        //            {
        //                XmlNodeList nodes = xmlDoc.SelectNodes(
        //                    "//asmv1:assembly/asmv2:dependency/asmv2:dependentAssembly",
        //                    xmlNamespaceManager)!;

        //                foreach (XmlNode node in nodes)
        //                {
        //                    XmlNode duplicateNode = node.CloneNode(deep: true);

        //                    node.ParentNode!.AppendChild(duplicateNode);
        //                }
        //            });
        //        FileInfo file = CreateFile(temporaryDirectory.Directory, xml);

        //        bool actualResult = _signer.TryGetApplicationManifestFileName(file, out string? applicationManifestFileName);

        //        Assert.False(actualResult);
        //    }
        //}

        //[Theory]
        //[InlineData(@"MyApp_1_0_0_0\MyApp.dll.manifest")]
        //[InlineData("https://unit.test/MyApp.dll.manifest")]
        //public void TryGetApplicationManifestFileName_WhenCodeBaseIsValid_ReturnsTrue(string codebase)
        //{
        //    using (TemporaryDirectory temporaryDirectory = new(_directoryService))
        //    {
        //        string xml = string.Format(DeploymentManifestCodeBaseTemplate, codebase);
        //        FileInfo file = CreateFile(temporaryDirectory.Directory, xml);

        //        bool actualResult = _signer.TryGetApplicationManifestFileName(file, out string? applicationManifestFileName);

        //        Assert.True(actualResult);
        //        Assert.Equal("MyApp.dll.manifest", applicationManifestFileName);
        //    }
        //}

        [Fact]
        public async Task SignAsync_WhenStandaloneApplicationManifest_DoesNotHoldFileLock()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);

            // Create a properly formed standalone application manifest using ManifestWriter
            // so that ManifestReader.ReadManifest classifies it as an ApplicationManifest.
            FileInfo manifestFile = CreateApplicationManifest(
                temporaryDirectory.Directory, "App", new Version(1, 0, 0, 0));

            SignOptions options = new(
                "TestApp",
                "TestPublisher",
                "TestDescription",
                new Uri("https://description.test"),
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256,
                new Uri("http://timestamp.test"),
                matcher: null,
                antiMatcher: null,
                recurseContainers: false,
                noSignClickOnceDeps: false,
                noUpdateClickOnceManifest: true,
                signedFileTracker: new SignedFileTracker());

            using X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate();
            using RSA privateKey = certificate.GetRSAPrivateKey()!;

            Mock<ISignatureAlgorithmProvider> signatureAlgorithmProvider = new();
            Mock<ICertificateProvider> certificateProvider = new();

            certificateProvider.Setup(x => x.GetCertificateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(certificate);

            signatureAlgorithmProvider.Setup(x => x.GetRsaAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(privateKey);

            Mock<IServiceProvider> serviceProvider = new();
            serviceProvider.Setup(x => x.GetService(It.IsAny<Type>()))
                .Returns(Mock.Of<IAggregatingDataFormatSigner>());

            bool fileWasWritableDuringMageCall = false;
            Mock<IMageCli> mageCli = new();
            string expectedArgs = $@"-update ""{manifestFile.FullName}"" -a sha256RSA -n ""TestApp""";
            mageCli.Setup(x => x.RunAsync(
                    It.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))))
                .Callback<string>(_ =>
                {
                    // Verify the file is NOT locked by trying to open it for writing.
                    // Before the fix, this would throw because the FileStream from
                    // OpenRead() was still held open during this call.
                    using FileStream fs = new(manifestFile.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    fileWasWritableDuringMageCall = true;
                })
                .ReturnsAsync(0);

            Mock<IManifestSigner> manifestSigner = new();
            Mock<IFileMatcher> fileMatcher = new();
            ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();

            ClickOnceSigner2 signer = new(
                signatureAlgorithmProvider.Object,
                certificateProvider.Object,
                serviceProvider.Object,
                mageCli.Object,
                manifestSigner.Object,
                fileMatcher.Object,
                _clickOnceAppFactory,
                new ManifestReaderAdapter(),
                logger);

            signer.Retry = TimeSpan.FromMicroseconds(1);

            await signer.SignAsync([manifestFile], options);

            Assert.True(fileWasWritableDuringMageCall, "The manifest file should not be locked when mage.exe is invoked.");
            mageCli.VerifyAll();
        }

        private static FileInfo AddFile(
            ContainerSpy containerSpy,
            DirectoryInfo directory,
            string fileContent,
            params string[] fileParts)
        {
            string[] parts = [directory.FullName, .. fileParts];
            FileInfo file = new(Path.Combine(parts));

            // The file needs to exist because it will be renamed.
            file.Directory!.Create();
            File.WriteAllText(file.FullName, fileContent);

            containerSpy.Files.Add(file);

            return file;
        }

        private static FileInfo CreateApplicationManifest(DirectoryInfo directory, string appName, Version version)
        {
            ArgumentNullException.ThrowIfNull(directory, nameof(directory));

            directory.Create();

            ApplicationManifest applicationManifest = new()
            {
                AssemblyIdentity = new AssemblyIdentity
                {
                    Name = appName,
                    Version = version.ToString(),
                    PublicKeyToken = "0000000000000000",
                    Culture = "neutral",
                    ProcessorArchitecture = "msil"
                },
                Description = "Contoso Limited"
            };

            string fileName = $"{appName}.dll";
            string assemblyPath = Path.Combine(directory.FullName, fileName);
            applicationManifest.AssemblyReferences.Add(new AssemblyReference
            {
                TargetPath = fileName,
                IsOptional = false,
                AssemblyIdentity = new AssemblyIdentity
                {
                    Name = fileName,
                    Version = version.ToString(),
                    PublicKeyToken = "0000000000000000",
                    Culture = "neutral",
                    ProcessorArchitecture = "msil"
                }
            });

            string applicationManifestPath = Path.Combine(directory.FullName, $"{fileName}.manifest");
            applicationManifest.SourcePath = applicationManifestPath;

            ManifestWriter.WriteManifest(applicationManifest);

            return new FileInfo(applicationManifestPath);
        }

        private static FileInfo CreateDeploymentManifest(
            DirectoryInfo directory,
            string appName,
            Version version,
            FileInfo applicationManifestFile)
        {
            ArgumentNullException.ThrowIfNull(directory, nameof(directory));
            ArgumentNullException.ThrowIfNull(applicationManifestFile, nameof(applicationManifestFile));

            directory.Create();

            DeployManifest deploymentManifest = new()
            {
                AssemblyIdentity = new AssemblyIdentity
                {
                    Name = $"{appName}.application",
                    Version = version.ToString(),
                    PublicKeyToken = "0000000000000000",
                    Culture = "neutral",
                    ProcessorArchitecture = "msil"
                },
                Description = "Contoso Limited",
                MapFileExtensions = true
            };

            string relativePath = Path.GetRelativePath(directory.FullName, applicationManifestFile.FullName);

            deploymentManifest.EntryPoint = new AssemblyReference()
            {
                TargetPath = relativePath,
                AssemblyIdentity = new AssemblyIdentity()
                {
                    Name = appName,
                    Version = version.ToString(),
                    PublicKeyToken = "0000000000000000",
                    Culture = "neutral",
                    ProcessorArchitecture = "msil"
                }
            };

            deploymentManifest.AssemblyReferences.Add(new AssemblyReference
            {
                TargetPath = relativePath,
                AssemblyIdentity = AssemblyIdentity.FromManifest(applicationManifestFile.FullName)
            });

            string deploymentManifestPath = Path.Combine(directory.FullName, $"{appName}.application");
            deploymentManifest.SourcePath = deploymentManifestPath;

            ManifestWriter.WriteManifest(deploymentManifest);

            return new FileInfo(deploymentManifestPath);
        }

        [Fact]
        public async Task SignAsync_WithNoSignClickOnceDeps_SignsOnlyDeploymentManifest()
        {
            const string commonName = "Test certificate (DO NOT TRUST)";

            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                TestClickOnceApp testApp = TestClickOnceApp.Create(temporaryDirectory.Directory,
                    additionalFilesCreator: applicationDirectory =>
                    {
                        FileInfo file = new(Path.Combine(applicationDirectory.FullName, "App.dll"));

                        TestClickOnceApp.CreateAssembly("class A { }", Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, file);

                        file = new(Path.Combine(applicationDirectory.FullName, "App.dll.manifest"));

                        // This is an incomplete manifest --- just enough to satisfy SignAsync(...)'s requirements.
                        File.WriteAllText(file.FullName, @$"<?xml version=""1.0"" encoding=""utf-8""?>
<asmv1:assembly xsi:schemaLocation=""urn:schemas-microsoft-com:asm.v1 assembly.adaptive.xsd"" manifestVersion=""1.0"" xmlns:asmv1=""urn:schemas-microsoft-com:asm.v1"" xmlns=""urn:schemas-microsoft-com:asm.v2"" xmlns:asmv2=""urn:schemas-microsoft-com:asm.v2"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:co.v1=""urn:schemas-microsoft-com:clickonce.v1"" xmlns:asmv3=""urn:schemas-microsoft-com:asm.v3"" xmlns:dsig=""http://www.w3.org/2000/09/xmldsig#"" xmlns:co.v2=""urn:schemas-microsoft-com:clickonce.v2"">
  <publisherIdentity name=""CN={commonName}, O=unit.test"" />
</asmv1:assembly>");
                        file = new(Path.Combine(applicationDirectory.FullName, "App.json"));

                        File.WriteAllText(file.FullName, "{}");
                    });

                SignOptions options = new(
                    testApp.Name,
                    publisherName: null,
                    testApp.Description,
                    new Uri("https://description.test"),
                    HashAlgorithmName.SHA256,
                    HashAlgorithmName.SHA256,
                    new Uri("http://timestamp.test"),
                    matcher: null,
                    antiMatcher: null,
                    recurseContainers: false,
                    noSignClickOnceDeps: true,
                    noUpdateClickOnceManifest: false,
                    signedFileTracker: new SignedFileTracker());

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
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
                    string expectedArgs = $"-update \"{testApp.ApplicationManifestFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\"";
                    mageCli.Setup(x => x.RunAsync(
                            It.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))))
                        .ReturnsAsync(0);

                    string publisher = certificate.SubjectName.Name;

                    expectedArgs = $"-update \"{testApp.DeploymentManifestFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\" -pub \"{publisher}\" -appm \"{testApp.ApplicationManifestFile.FullName}\" -SupportURL https://description.test/";
                    mageCli.Setup(x => x.RunAsync(
                            It.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))))
                        .ReturnsAsync(0);

                    Mock<IManifestSigner> manifestSigner = new();
                    Mock<IFileMatcher> fileMatcher = new();

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.DeploymentManifestFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.ApplicationManifestFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();
                    ClickOnceSigner2 signer = new(
                        signatureAlgorithmProvider.Object,
                        certificateProvider.Object,
                        serviceProvider.Object,
                        mageCli.Object,
                        manifestSigner.Object,
                        fileMatcher.Object,
                        _clickOnceAppFactory,
                        new ManifestReaderAdapter(),
                        logger);

                    await signer.SignAsync([testApp.DeploymentManifestFile], options);

                    // Verify that payload files were NOT signed (aggregating signer should not have been called)
                    Assert.Empty(aggregatingSignerSpy.FilesSubmittedForSigning);

                    // Verify that the application manifest was NOT signed (it is a dependency, not an explicit input)
                    manifestSigner.Verify(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.ApplicationManifestFile.Name),
                            It.IsAny<X509Certificate2>(),
                            It.IsAny<RSA>(),
                            It.IsAny<SignOptions>()),
                        Times.Never);

                    // Verify that only the deployment manifest was signed
                    manifestSigner.Verify(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.DeploymentManifestFile.Name),
                            It.IsAny<X509Certificate2>(),
                            It.IsAny<RSA>(),
                            It.IsAny<SignOptions>()),
                        Times.Once);

                    // Verify that mage was called only for the deployment manifest
                    // (the -appm parameter references the application manifest path but does not sign it)
                    mageCli.Verify(
                        x => x.RunAsync(It.Is<string>(args => args.Contains(testApp.DeploymentManifestFile.FullName))),
                        Times.Once);
                }
            }
        }

        [Fact]
        public async Task SignAsync_WithNoUpdateClickOnceManifest_SkipsManifestUpdates()
        {
            const string commonName = "Test certificate (DO NOT TRUST)";

            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                TestClickOnceApp testApp = TestClickOnceApp.Create(temporaryDirectory.Directory,
                    additionalFilesCreator: applicationDirectory =>
                    {
                        FileInfo file = new(Path.Combine(applicationDirectory.FullName, "App.dll"));

                        TestClickOnceApp.CreateAssembly("class A { }", Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, file);

                        file = new(Path.Combine(applicationDirectory.FullName, "App.dll.manifest"));

                        // This is an incomplete manifest --- just enough to satisfy SignAsync(...)'s requirements.
                        File.WriteAllText(file.FullName, @$"<?xml version=""1.0"" encoding=""utf-8""?>
<asmv1:assembly xsi:schemaLocation=""urn:schemas-microsoft-com:asm.v1 assembly.adaptive.xsd"" manifestVersion=""1.0"" xmlns:asmv1=""urn:schemas-microsoft-com:asm.v1"" xmlns=""urn:schemas-microsoft-com:asm.v2"" xmlns:asmv2=""urn:schemas-microsoft-com:asm.v2"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:co.v1=""urn:schemas-microsoft-com:clickonce.v1"" xmlns:asmv3=""urn:schemas-microsoft-com:asm.v3"" xmlns:dsig=""http://www.w3.org/2000/09/xmldsig#"" xmlns:co.v2=""urn:schemas-microsoft-com:clickonce.v2"">
  <publisherIdentity name=""CN={commonName}, O=unit.test"" />
</asmv1:assembly>");
                        file = new(Path.Combine(applicationDirectory.FullName, "App.json"));

                        File.WriteAllText(file.FullName, "{}");
                    });

                SignOptions options = new(
                    testApp.Name,
                    publisherName: null,
                    testApp.Description,
                    new Uri("https://description.test"),
                    HashAlgorithmName.SHA256,
                    HashAlgorithmName.SHA256,
                    new Uri("http://timestamp.test"),
                    matcher: null,
                    antiMatcher: null,
                    recurseContainers: false,
                    noSignClickOnceDeps: false,
                    noUpdateClickOnceManifest: true,
                    signedFileTracker: new SignedFileTracker());

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
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

                    // Only the deployment manifest should be signed via mage
                    string expectedArgs = $"-update \"{testApp.DeploymentManifestFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\" -pub \"{publisher}\" -appm \"{testApp.ApplicationManifestFile.FullName}\" -SupportURL https://description.test/";
                    mageCli.Setup(x => x.RunAsync(
                            It.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))))
                        .ReturnsAsync(0);

                    Mock<IManifestSigner> manifestSigner = new();
                    Mock<IFileMatcher> fileMatcher = new();

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.DeploymentManifestFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();
                    ClickOnceSigner2 signer = new(
                        signatureAlgorithmProvider.Object,
                        certificateProvider.Object,
                        serviceProvider.Object,
                        mageCli.Object,
                        manifestSigner.Object,
                        fileMatcher.Object,
                        _clickOnceAppFactory,
                        new ManifestReaderAdapter(),
                        logger);

                    await signer.SignAsync([testApp.DeploymentManifestFile], options);

                    // Verify that payload files were NOT signed (no discovery of dependencies)
                    Assert.Empty(aggregatingSignerSpy.FilesSubmittedForSigning);

                    // Verify that the application manifest was NOT signed (it is a dependency)
                    manifestSigner.Verify(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.ApplicationManifestFile.Name),
                            It.IsAny<X509Certificate2>(),
                            It.IsAny<RSA>(),
                            It.IsAny<SignOptions>()),
                        Times.Never);

                    // Verify that only the deployment manifest was signed
                    manifestSigner.Verify(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.DeploymentManifestFile.Name),
                            It.IsAny<X509Certificate2>(),
                            It.IsAny<RSA>(),
                            It.IsAny<SignOptions>()),
                        Times.Once);

                    // Verify mage was called only for the deployment manifest
                    mageCli.Verify(
                        x => x.RunAsync(It.Is<string>(args => args.Contains(testApp.DeploymentManifestFile.FullName))),
                        Times.Once);
                }
            }
        }

        [Fact]
        public async Task SignAsync_WithBothOptions_SkipsUpdatesAndDependencySigning()
        {
            const string commonName = "Test certificate (DO NOT TRUST)";

            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                TestClickOnceApp testApp = TestClickOnceApp.Create(temporaryDirectory.Directory,
                    additionalFilesCreator: applicationDirectory =>
                    {
                        FileInfo file = new(Path.Combine(applicationDirectory.FullName, "App.dll"));

                        TestClickOnceApp.CreateAssembly("class A { }", Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, file);

                        file = new(Path.Combine(applicationDirectory.FullName, "App.dll.manifest"));

                        // This is an incomplete manifest --- just enough to satisfy SignAsync(...)'s requirements.
                        File.WriteAllText(file.FullName, @$"<?xml version=""1.0"" encoding=""utf-8""?>
<asmv1:assembly xsi:schemaLocation=""urn:schemas-microsoft-com:asm.v1 assembly.adaptive.xsd"" manifestVersion=""1.0"" xmlns:asmv1=""urn:schemas-microsoft-com:asm.v1"" xmlns=""urn:schemas-microsoft-com:asm.v2"" xmlns:asmv2=""urn:schemas-microsoft-com:asm.v2"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:co.v1=""urn:schemas-microsoft-com:clickonce.v1"" xmlns:asmv3=""urn:schemas-microsoft-com:asm.v3"" xmlns:dsig=""http://www.w3.org/2000/09/xmldsig#"" xmlns:co.v2=""urn:schemas-microsoft-com:clickonce.v2"">
  <publisherIdentity name=""CN={commonName}, O=unit.test"" />
</asmv1:assembly>");
                        file = new(Path.Combine(applicationDirectory.FullName, "App.json"));

                        File.WriteAllText(file.FullName, "{}");
                    });

                SignOptions options = new(
                    testApp.Name,
                    publisherName: null,
                    testApp.Description,
                    new Uri("https://description.test"),
                    HashAlgorithmName.SHA256,
                    HashAlgorithmName.SHA256,
                    new Uri("http://timestamp.test"),
                    matcher: null,
                    antiMatcher: null,
                    recurseContainers: false,
                    noSignClickOnceDeps: true,
                    noUpdateClickOnceManifest: true,
                    signedFileTracker: new SignedFileTracker());

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
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

                    // Only the deployment manifest should be signed via mage
                    string expectedArgs = $"-update \"{testApp.DeploymentManifestFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\" -pub \"{publisher}\" -appm \"{testApp.ApplicationManifestFile.FullName}\" -SupportURL https://description.test/";
                    mageCli.Setup(x => x.RunAsync(
                            It.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))))
                        .ReturnsAsync(0);

                    Mock<IManifestSigner> manifestSigner = new();
                    Mock<IFileMatcher> fileMatcher = new();

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.DeploymentManifestFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();
                    ClickOnceSigner2 signer = new(
                        signatureAlgorithmProvider.Object,
                        certificateProvider.Object,
                        serviceProvider.Object,
                        mageCli.Object,
                        manifestSigner.Object,
                        fileMatcher.Object,
                        _clickOnceAppFactory,
                        new ManifestReaderAdapter(),
                        logger);

                    await signer.SignAsync([testApp.DeploymentManifestFile], options);

                    // Verify that payload files were NOT signed (no discovery of dependencies)
                    Assert.Empty(aggregatingSignerSpy.FilesSubmittedForSigning);

                    // Verify that the application manifest was NOT signed (it is a dependency)
                    manifestSigner.Verify(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.ApplicationManifestFile.Name),
                            It.IsAny<X509Certificate2>(),
                            It.IsAny<RSA>(),
                            It.IsAny<SignOptions>()),
                        Times.Never);

                    // Verify that only the deployment manifest was signed
                    manifestSigner.Verify(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.DeploymentManifestFile.Name),
                            It.IsAny<X509Certificate2>(),
                            It.IsAny<RSA>(),
                            It.IsAny<SignOptions>()),
                        Times.Once);
                }
            }
        }

        [Fact]
        public async Task SignAsync_WhenStandaloneManifestWithNoUpdateClickOnceManifest_SkipsPayloadDiscoveryAndSigning()
        {
            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                TestClickOnceApp testApp = TestClickOnceApp.Create(temporaryDirectory.Directory, mapFileExtensions: false);

                SignOptions options = new(
                    testApp.Name,
                    publisherName: null,
                    testApp.Description,
                    new Uri("https://description.test"),
                    HashAlgorithmName.SHA256,
                    HashAlgorithmName.SHA256,
                    new Uri("http://timestamp.test"),
                    matcher: null,
                    antiMatcher: null,
                    recurseContainers: false,
                    noSignClickOnceDeps: false,
                    noUpdateClickOnceManifest: true,
                    signedFileTracker: new SignedFileTracker());

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
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

                    string expectedArgs = $"-update \"{testApp.ApplicationManifestFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\"";
                    mageCli.Setup(x => x.RunAsync(
                            It.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))))
                        .ReturnsAsync(0);

                    Mock<IManifestSigner> manifestSigner = new();
                    Mock<IFileMatcher> fileMatcher = new();

                    manifestSigner.Setup(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.ApplicationManifestFile.Name),
                            It.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                            It.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                            It.Is<SignOptions>(o => ReferenceEquals(options, o))));

                    ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();
                    ClickOnceSigner2 signer = new(
                        signatureAlgorithmProvider.Object,
                        certificateProvider.Object,
                        serviceProvider.Object,
                        mageCli.Object,
                        manifestSigner.Object,
                        fileMatcher.Object,
                        _clickOnceAppFactory,
                        new ManifestReaderAdapter(),
                        logger);

                    await signer.SignAsync([testApp.ApplicationManifestFile], options);

                    // Verify that payload files were NOT signed (no discovery of dependencies)
                    Assert.Empty(aggregatingSignerSpy.FilesSubmittedForSigning);

                    // Verify that the application manifest WAS signed
                    manifestSigner.Verify(
                        x => x.Sign(
                            It.Is<FileInfo>(fi => fi.Name == testApp.ApplicationManifestFile.Name),
                            It.IsAny<X509Certificate2>(),
                            It.IsAny<RSA>(),
                            It.IsAny<SignOptions>()),
                        Times.Once);

                    mageCli.VerifyAll();
                }
            }
        }

        [Fact]
        public async Task SignAsync_WhenDeployExtensionsMapped_CallsUpdateFileInfoBeforeRestoringDeployExtensions()
        {
            const string commonName = "Test certificate (DO NOT TRUST)";

            using (TemporaryDirectory temporaryDirectory = new(_directoryService))
            {
                TestClickOnceApp testApp = TestClickOnceApp.Create(temporaryDirectory.Directory,
                    additionalFilesCreator: applicationDirectory =>
                    {
                        FileInfo dllFile = new(Path.Combine(applicationDirectory.FullName, "App.dll"));
                        TestClickOnceApp.CreateAssembly("class A { }", Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, dllFile);

                        FileInfo manifestFile = new(Path.Combine(applicationDirectory.FullName, "App.dll.manifest"));
                        File.WriteAllText(manifestFile.FullName, @$"<?xml version=""1.0"" encoding=""utf-8""?>
<asmv1:assembly xsi:schemaLocation=""urn:schemas-microsoft-com:asm.v1 assembly.adaptive.xsd"" manifestVersion=""1.0"" xmlns:asmv1=""urn:schemas-microsoft-com:asm.v1"" xmlns=""urn:schemas-microsoft-com:asm.v2"" xmlns:asmv2=""urn:schemas-microsoft-com:asm.v2"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:co.v1=""urn:schemas-microsoft-com:clickonce.v1"" xmlns:asmv3=""urn:schemas-microsoft-com:asm.v3"" xmlns:dsig=""http://www.w3.org/2000/09/xmldsig#"" xmlns:co.v2=""urn:schemas-microsoft-com:clickonce.v2"">
  <publisherIdentity name=""CN={commonName}, O=unit.test"" />
</asmv1:assembly>");

                        FileInfo jsonFile = new(Path.Combine(applicationDirectory.FullName, "App.json"));
                        File.WriteAllText(jsonFile.FullName, "{}");
                    });

                // Wrap the real factory with a spy that intercepts UpdateFileInfo()
                ClickOnceAppFactorySpy factorySpy = new(_clickOnceAppFactory);

                SignOptions options = new(
                    testApp.Name,
                    testApp.Publisher,
                    testApp.Description,
                    new Uri("https://description.test"),
                    HashAlgorithmName.SHA256,
                    HashAlgorithmName.SHA256,
                    new Uri("http://timestamp.test"),
                    matcher: null,
                    antiMatcher: null,
                    recurseContainers: false,
                    noSignClickOnceDeps: false,
                    noUpdateClickOnceManifest: false,
                    signedFileTracker: new SignedFileTracker());

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
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
                    mageCli.Setup(x => x.RunAsync(It.IsAny<string>()))
                        .ReturnsAsync(0);

                    Mock<IManifestSigner> manifestSigner = new();
                    Mock<IFileMatcher> fileMatcher = new();

                    ILogger<IDataFormatSigner> logger = Mock.Of<ILogger<IDataFormatSigner>>();
                    ClickOnceSigner2 signer = new(
                        signatureAlgorithmProvider.Object,
                        certificateProvider.Object,
                        serviceProvider.Object,
                        mageCli.Object,
                        manifestSigner.Object,
                        fileMatcher.Object,
                        factorySpy,
                        new ManifestReaderAdapter(),
                        logger);

                    await signer.SignAsync([testApp.DeploymentManifestFile], options);

                    // Verify that UpdateFileInfo was called while .deploy files were absent.
                    // Per spec (Appendix B step 13): UpdateFileInfo() hashes files at their
                    // ResolvedPath, which does not include .deploy; the suffixes must be absent
                    // when hashes are computed.
                    Assert.NotNull(factorySpy.AppWrapper);
                    Assert.NotNull(factorySpy.AppWrapper.ManifestSpy);
                    Assert.True(factorySpy.AppWrapper.ManifestSpy.UpdateFileInfoCalled,
                        "UpdateFileInfo() should have been called.");
                    Assert.False(factorySpy.AppWrapper.ManifestSpy.DeployFilesPresentDuringUpdateFileInfo,
                        ".deploy files should be absent when UpdateFileInfo() is called.");
                }
            }
        }

        /// <summary>
        /// Wraps an <see cref="IApplicationManifest"/> and records whether .deploy files
        /// are present on disk when <see cref="UpdateFileInfo"/> is called.
        /// </summary>
        private sealed class ApplicationManifestSpy : IApplicationManifest
        {
            private readonly IApplicationManifest _inner;
            private readonly DirectoryInfo _applicationDirectory;

            internal bool UpdateFileInfoCalled { get; private set; }
            internal bool DeployFilesPresentDuringUpdateFileInfo { get; private set; }

            internal ApplicationManifestSpy(IApplicationManifest inner, DirectoryInfo applicationDirectory)
            {
                _inner = inner;
                _applicationDirectory = applicationDirectory;
            }

            public AssemblyReferenceCollection AssemblyReferences => _inner.AssemblyReferences;
            public FileReferenceCollection FileReferences => _inner.FileReferences;
            public OutputMessageCollection OutputMessages => _inner.OutputMessages;
            public bool ReadOnly { get => _inner.ReadOnly; set => _inner.ReadOnly = value; }

            public void ResolveFiles(string[] searchPaths) => _inner.ResolveFiles(searchPaths);

            public void UpdateFileInfo()
            {
                UpdateFileInfoCalled = true;
                DeployFilesPresentDuringUpdateFileInfo = _applicationDirectory.GetFiles("*.deploy").Length > 0;
                _inner.UpdateFileInfo();
            }
        }

        /// <summary>
        /// Wraps an <see cref="IClickOnceApp"/> and replaces its <see cref="IClickOnceApp.ApplicationManifest"/>
        /// with an <see cref="ApplicationManifestSpy"/>.
        /// </summary>
        private sealed class ClickOnceAppWrapper : IClickOnceApp
        {
            private readonly IClickOnceApp _inner;

            internal ApplicationManifestSpy? ManifestSpy { get; }

            internal ClickOnceAppWrapper(IClickOnceApp inner)
            {
                _inner = inner;

                if (inner.ApplicationManifest is not null && inner.ApplicationManifestFile?.Directory is not null)
                {
                    ManifestSpy = new ApplicationManifestSpy(inner.ApplicationManifest, inner.ApplicationManifestFile.Directory);
                }
            }

            public IApplicationManifest? ApplicationManifest => ManifestSpy ?? _inner.ApplicationManifest;
            public FileInfo? ApplicationManifestFile => _inner.ApplicationManifestFile;
            public IDeployManifest DeploymentManifest => _inner.DeploymentManifest;
            public FileInfo DeploymentManifestFile => _inner.DeploymentManifestFile;
            public IEnumerable<FileInfo> GetPayloadFiles() => _inner.GetPayloadFiles();
        }

        /// <summary>
        /// Wraps an <see cref="IClickOnceAppFactory"/> and returns a <see cref="ClickOnceAppWrapper"/>
        /// so that <see cref="IApplicationManifest.UpdateFileInfo"/> calls can be intercepted.
        /// </summary>
        private sealed class ClickOnceAppFactorySpy : IClickOnceAppFactory
        {
            private readonly IClickOnceAppFactory _inner;

            internal ClickOnceAppWrapper? AppWrapper { get; private set; }

            internal ClickOnceAppFactorySpy(IClickOnceAppFactory inner)
            {
                _inner = inner;
            }

            public bool TryReadFromDeploymentManifest(
                FileInfo deploymentManifestFile,
                ILogger logger,
                [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IClickOnceApp? clickOnceApp)
            {
                if (_inner.TryReadFromDeploymentManifest(deploymentManifestFile, logger, out IClickOnceApp? realApp))
                {
                    AppWrapper = new ClickOnceAppWrapper(realApp!);
                    clickOnceApp = AppWrapper;
                    return true;
                }

                clickOnceApp = null;
                return false;
            }
        }
    }
}
