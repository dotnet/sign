// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed class ClickOnceSignerTests : IDisposable
    {
        private readonly DirectoryService _directoryService;
        private readonly ClickOnceSigner _signer;

        public ClickOnceSignerTests()
        {
            _directoryService = new(Substitute.For<ILogger<IDirectoryService>>());
            _signer = new ClickOnceSigner(
                Substitute.For<ISignatureAlgorithmProvider>(),
                Substitute.For<ICertificateProvider>(),
                Substitute.For<IServiceProvider>(),
                Substitute.For<IMageCli>(),
                Substitute.For<IManifestSigner>(),
                Substitute.For<ILogger<IDataFormatSigner>>(),
                Substitute.For<IFileMatcher>());
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
                    Substitute.For<ICertificateProvider>(),
                    Substitute.For<IServiceProvider>(),
                    Substitute.For<IMageCli>(),
                    Substitute.For<IManifestSigner>(),
                    Substitute.For<ILogger<IDataFormatSigner>>(),
                    Substitute.For<IFileMatcher>()));

            Assert.Equal("signatureAlgorithmProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner(
                    Substitute.For<ISignatureAlgorithmProvider>(),
                    certificateProvider: null!,
                    Substitute.For<IServiceProvider>(),
                    Substitute.For<IMageCli>(),
                    Substitute.For<IManifestSigner>(),
                    Substitute.For<ILogger<IDataFormatSigner>>(),
                    Substitute.For<IFileMatcher>()));

            Assert.Equal("certificateProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner(
                    Substitute.For<ISignatureAlgorithmProvider>(),
                    Substitute.For<ICertificateProvider>(),
                    serviceProvider: null!,
                    Substitute.For<IMageCli>(),
                    Substitute.For<IManifestSigner>(),
                    Substitute.For<ILogger<IDataFormatSigner>>(),
                    Substitute.For<IFileMatcher>()));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenMageCliIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner(
                    Substitute.For<ISignatureAlgorithmProvider>(),
                    Substitute.For<ICertificateProvider>(),
                    Substitute.For<IServiceProvider>(),
                    mageCli: null!,
                    Substitute.For<IManifestSigner>(),
                    Substitute.For<ILogger<IDataFormatSigner>>(),
                    Substitute.For<IFileMatcher>()));

            Assert.Equal("mageCli", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenManifestSignerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner(
                    Substitute.For<ISignatureAlgorithmProvider>(),
                    Substitute.For<ICertificateProvider>(),
                    Substitute.For<IServiceProvider>(),
                    Substitute.For<IMageCli>(),
                    manifestSigner: null!,
                    Substitute.For<ILogger<IDataFormatSigner>>(),
                    Substitute.For<IFileMatcher>()));

            Assert.Equal("manifestSigner", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner(
                    Substitute.For<ISignatureAlgorithmProvider>(),
                    Substitute.For<ICertificateProvider>(),
                    Substitute.For<IServiceProvider>(),
                    Substitute.For<IMageCli>(),
                    Substitute.For<IManifestSigner>(),
                    logger: null!,
                    Substitute.For<IFileMatcher>()));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenFileMatcherIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ClickOnceSigner(
                    Substitute.For<ISignatureAlgorithmProvider>(),
                    Substitute.For<ICertificateProvider>(),
                    Substitute.For<IServiceProvider>(),
                    Substitute.For<IMageCli>(),
                    Substitute.For<IManifestSigner>(),
                    Substitute.For<ILogger<IDataFormatSigner>>(),
                    fileMatcher: null!));

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
        public void RequiresOriginalFileName_ReturnsTrue()
        {
            IDataFormatSigner signer = _signer;

            Assert.True(signer.RequiresOriginalFileName);
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

        [Fact]
        public async Task SignAsync_WhenSigningFails_Throws()
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

                SignOptions options = new(
                    "ApplicationName",
                    "PublisherName",
                    "Description",
                    new Uri("https://description.test"),
                    HashAlgorithmName.SHA256,
                    HashAlgorithmName.SHA256,
                    new Uri("http://timestamp.test"),
                    matcher: null,
                    antiMatcher: null,
                    recurseContainers: true);

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
                using (RSA privateKey = certificate.GetRSAPrivateKey()!)
                {
                    ISignatureAlgorithmProvider signatureAlgorithmProvider = Substitute.For<ISignatureAlgorithmProvider>();
                    ICertificateProvider certificateProvider = Substitute.For<ICertificateProvider>();
                    certificateProvider.GetCertificateAsync(Arg.Any<CancellationToken>()).Returns(certificate);
                    signatureAlgorithmProvider.GetRsaAsync(Arg.Any<CancellationToken>()).Returns(privateKey);

                    IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
                    AggregatingSignerSpy aggregatingSignerSpy = new();
                    serviceProvider.GetService(Arg.Any<Type>()).Returns(aggregatingSignerSpy);

                    IMageCli mageCli = Substitute.For<IMageCli>();
                    mageCli.RunAsync(Arg.Any<string>()).Returns(1);

                    IManifestSigner manifestSigner = Substitute.For<IManifestSigner>();
                    IFileMatcher fileMatcher = Substitute.For<IFileMatcher>();
                    ILogger<IDataFormatSigner> logger = Substitute.For<ILogger<IDataFormatSigner>>();

                    ClickOnceSigner signer = new(
                        signatureAlgorithmProvider,
                        certificateProvider,
                        serviceProvider,
                        mageCli,
                        manifestSigner,
                        logger,
                        fileMatcher);

                    signer.Retry = TimeSpan.FromMicroseconds(1);

                    await Assert.ThrowsAsync<SigningException>(() => signer.SignAsync(new[] { applicationFile }, options));
                }
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("PublisherName")]
        public async Task SignAsync_WhenFilesIsClickOnceFile_Signs(string? publisherName)
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
                    antiMatcher: null,
                    recurseContainers: true);

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
                using (RSA privateKey = certificate.GetRSAPrivateKey()!)
                {
                    ISignatureAlgorithmProvider signatureAlgorithmProvider = Substitute.For<ISignatureAlgorithmProvider>();
                    ICertificateProvider certificateProvider = Substitute.For<ICertificateProvider>();
                    certificateProvider.GetCertificateAsync(Arg.Any<CancellationToken>()).Returns(certificate);
                    signatureAlgorithmProvider.GetRsaAsync(Arg.Any<CancellationToken>()).Returns(privateKey);

                    IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
                    AggregatingSignerSpy aggregatingSignerSpy = new();
                    serviceProvider.GetService(Arg.Any<Type>()).Returns(aggregatingSignerSpy);

                    IMageCli mageCli = Substitute.For<IMageCli>();
                    string expectedArgs = $"-update \"{manifestFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\"";
                    mageCli.RunAsync(Arg.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))).Returns(0);

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
                    mageCli.RunAsync(Arg.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))).Returns(0);

                    IManifestSigner manifestSigner = Substitute.For<IManifestSigner>();
                    IFileMatcher fileMatcher = Substitute.For<IFileMatcher>();
                    ILogger<IDataFormatSigner> logger = Substitute.For<ILogger<IDataFormatSigner>>();
                    ClickOnceSigner signer = new(
                        signatureAlgorithmProvider,
                        certificateProvider,
                        serviceProvider,
                        mageCli,
                        manifestSigner,
                        logger,
                        fileMatcher);

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
                    await mageCli.Received(1).RunAsync(Arg.Is<string>(args => string.Equals(
                        $"-update \"{manifestFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\"",
                        args,
                        StringComparison.Ordinal)));
                    await mageCli.Received(1).RunAsync(Arg.Is<string>(args => string.Equals(
                        $"-update \"{applicationFile.FullName}\" -a sha256RSA -n \"{options.ApplicationName}\" -pub \"{publisher}\" -appm \"{manifestFile.FullName}\" -SupportURL https://description.test/",
                        args,
                        StringComparison.Ordinal)));
                    manifestSigner.Received(1).Sign(
                        Arg.Is<FileInfo>(fi => fi != null && fi.Name == manifestFile.Name),
                        Arg.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                        Arg.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                        Arg.Is<SignOptions>(o => ReferenceEquals(options, o)));
                    manifestSigner.Received(1).Sign(
                        Arg.Is<FileInfo>(fi => fi != null && fi.Name == applicationFile.Name),
                        Arg.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                        Arg.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                        Arg.Is<SignOptions>(o => ReferenceEquals(options, o)));
                    Assert.Equal(2, mageCli.ReceivedCalls().Count());
                    Assert.Equal(2, manifestSigner.ReceivedCalls().Count());
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
                    string.Empty,
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
                    antiMatcher: null,
                    recurseContainers: true);

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
                using (RSA privateKey = certificate.GetRSAPrivateKey()!)
                {
                    ISignatureAlgorithmProvider signatureAlgorithmProvider = Substitute.For<ISignatureAlgorithmProvider>();
                    ICertificateProvider certificateProvider = Substitute.For<ICertificateProvider>();
                    certificateProvider.GetCertificateAsync(Arg.Any<CancellationToken>()).Returns(certificate);
                    signatureAlgorithmProvider.GetRsaAsync(Arg.Any<CancellationToken>()).Returns(privateKey);

                    IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
                    AggregatingSignerSpy aggregatingSignerSpy = new();
                    serviceProvider.GetService(Arg.Any<Type>()).Returns(aggregatingSignerSpy);

                    IMageCli mageCli = Substitute.For<IMageCli>();

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
                    mageCli.RunAsync(Arg.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal))).Returns(0);

                    IManifestSigner manifestSigner = Substitute.For<IManifestSigner>();
                    IFileMatcher fileMatcher = Substitute.For<IFileMatcher>();
                    ILogger<IDataFormatSigner> logger = Substitute.For<ILogger<IDataFormatSigner>>();
                    ClickOnceSigner signer = new(
                        signatureAlgorithmProvider,
                        certificateProvider,
                        serviceProvider,
                        mageCli,
                        manifestSigner,
                        logger,
                        fileMatcher);

                    await signer.SignAsync(new[] { applicationFile }, options);

                    // Verify that files have been renamed back.
                    foreach (FileInfo file in containerSpy.Files)
                    {
                        file.Refresh();

                        Assert.True(file.Exists);
                    }

                    Assert.Empty(aggregatingSignerSpy.FilesSubmittedForSigning);
                    await mageCli.Received(1).RunAsync(Arg.Is<string>(args => string.Equals(expectedArgs, args, StringComparison.Ordinal)));
                    manifestSigner.Received(1).Sign(
                        Arg.Is<FileInfo>(fi => fi != null && fi.Name == applicationFile.Name),
                        Arg.Is<X509Certificate2>(c => ReferenceEquals(certificate, c)),
                        Arg.Is<RSA>(rsa => ReferenceEquals(privateKey, rsa)),
                        Arg.Is<SignOptions>(o => ReferenceEquals(options, o)));
                    Assert.Single(mageCli.ReceivedCalls());
                    Assert.Single(manifestSigner.ReceivedCalls());
                }
            }
        }

        [Fact]
        public async Task SignAsync_RecursivePayload_RetainsOriginalSourceIdentity()
        {
            using TemporaryDirectory temporaryDirectory =
                new(_directoryService);
            await using SigningOperationCoordinator coordinator =
                new(_directoryService);
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
            FileInfo deployFile = AddFile(
                containerSpy,
                temporaryDirectory.Directory,
                string.Empty,
                "MyApp_1_0_0_0",
                "MyApp.dll.deploy");
            string originalApplicationPath = Path.Combine(
                temporaryDirectory.Directory.Parent!.FullName,
                "original",
                applicationFile.Name);
            SigningFile source = new(
                applicationFile,
                SigningSourceIdentity.PhysicalFile(
                    originalApplicationPath));
            SigningSourceIdentity expectedPayloadIdentity =
                SigningSourceIdentity.PhysicalFile(
                    Path.Combine(
                        Path.GetDirectoryName(
                            originalApplicationPath)!,
                        "MyApp_1_0_0_0",
                        deployFile.Name));
            SignOptions options = new(
                applicationName: "ApplicationName",
                publisherName: "PublisherName",
                description: "Description",
                descriptionUrl:
                    new Uri("https://description.test"),
                fileHashAlgorithm: HashAlgorithmName.SHA256,
                timestampHashAlgorithm: HashAlgorithmName.SHA256,
                timestampService:
                    new Uri("http://timestamp.test"),
                matcher: null,
                antiMatcher: null,
                recurseContainers: true);
            using X509Certificate2 certificate =
                SelfIssuedCertificateCreator.CreateCertificate();
            using RSA privateKey =
                certificate.GetRSAPrivateKey()!;
            ISignatureAlgorithmProvider signatureAlgorithmProvider =
                Substitute.For<ISignatureAlgorithmProvider>();
            ICertificateProvider certificateProvider =
                Substitute.For<ICertificateProvider>();
            IServiceProvider serviceProvider =
                Substitute.For<IServiceProvider>();
            AggregatingSignerSpy aggregatingSignerSpy = new();
            IMageCli mageCli = Substitute.For<IMageCli>();

            certificateProvider
                .GetCertificateAsync(Arg.Any<CancellationToken>())
                .Returns(certificate);
            signatureAlgorithmProvider
                .GetRsaAsync(Arg.Any<CancellationToken>())
                .Returns(privateKey);
            serviceProvider
                .GetService(Arg.Any<Type>())
                .Returns(aggregatingSignerSpy);
            mageCli.RunAsync(Arg.Any<string>()).Returns(0);
            ClickOnceSigner signer = new(
                signatureAlgorithmProvider,
                certificateProvider,
                serviceProvider,
                mageCli,
                Substitute.For<IManifestSigner>(),
                Substitute.For<ILogger<IDataFormatSigner>>(),
                Substitute.For<IFileMatcher>());

            await signer.SignAsync(
                source,
                options,
                coordinator);

            Assert.Equal(
                expectedPayloadIdentity,
                Assert.Single(
                    aggregatingSignerSpy.SourceIdentities));
        }

        [Fact]
        public async Task SignAsync_OverlappingDeploymentManifests_SignOnceAndRestoreDeploySuffix()
        {
            using TemporaryDirectory temporaryDirectory =
                new(_directoryService);
            await using SigningOperationCoordinator coordinator =
                new(_directoryService);
            FileInfo firstApplication = new(
                Path.Combine(
                    temporaryDirectory.Directory.FullName,
                    "First.application"));
            FileInfo secondApplication = new(
                Path.Combine(
                    temporaryDirectory.Directory.FullName,
                    "Second.application"));
            FileInfo deployFile = new(
                Path.Combine(
                    temporaryDirectory.Directory.FullName,
                    "payload.dll.deploy"));

            File.WriteAllText(firstApplication.FullName, string.Empty);
            File.WriteAllText(secondApplication.FullName, string.Empty);
            File.WriteAllText(deployFile.FullName, "unsigned");

            using X509Certificate2 certificate =
                SelfIssuedCertificateCreator.CreateCertificate();
            using RSA privateKey =
                certificate.GetRSAPrivateKey()!;
            ISignatureAlgorithmProvider signatureAlgorithmProvider =
                Substitute.For<ISignatureAlgorithmProvider>();
            ICertificateProvider certificateProvider =
                Substitute.For<ICertificateProvider>();
            IServiceProvider serviceProvider =
                Substitute.For<IServiceProvider>();
            IMageCli mageCli = Substitute.For<IMageCli>();
            IDataFormatSigner payloadSigner =
                Substitute.For<IDataFormatSigner>();

            certificateProvider
                .GetCertificateAsync(Arg.Any<CancellationToken>())
                .Returns(certificate);
            signatureAlgorithmProvider
                .GetRsaAsync(Arg.Any<CancellationToken>())
                .Returns(privateKey);
            mageCli.RunAsync(Arg.Any<string>()).Returns(0);
            payloadSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(
                    call =>
                        call.Arg<FileInfo>().Extension.Equals(
                            ".dll",
                            StringComparison.OrdinalIgnoreCase));
            payloadSigner
                .SignAsync(
                    Arg.Any<IEnumerable<FileInfo>>(),
                    Arg.Any<SignOptions>())
                .Returns(
                    call =>
                    {
                        foreach (FileInfo payload in
                            call.Arg<IEnumerable<FileInfo>>())
                        {
                            File.AppendAllText(
                                payload.FullName,
                                "-signed");
                        }

                        return Task.CompletedTask;
                    });
            ClickOnceSigner clickOnceSigner = new(
                signatureAlgorithmProvider,
                certificateProvider,
                serviceProvider,
                mageCli,
                Substitute.For<IManifestSigner>(),
                Substitute.For<ILogger<IDataFormatSigner>>(),
                Substitute.For<IFileMatcher>());
            AggregatingSigner aggregatingSigner = new(
                new IDataFormatSigner[]
                {
                    clickOnceSigner,
                    payloadSigner
                },
                Substitute.For<IDefaultDataFormatSigner>(),
                Substitute.For<IContainerProvider>(),
                Substitute.For<IFileMetadataService>(),
                Substitute.For<IMatcherFactory>());

            serviceProvider
                .GetService(typeof(IAggregatingDataFormatSigner))
                .Returns(aggregatingSigner);
            SignOptions options = new(
                applicationName: "ApplicationName",
                publisherName: "PublisherName",
                description: "Description",
                descriptionUrl:
                    new Uri("https://description.test"),
                fileHashAlgorithm: HashAlgorithmName.SHA256,
                timestampHashAlgorithm: HashAlgorithmName.SHA256,
                timestampService:
                    new Uri("http://timestamp.test"),
                matcher: null,
                antiMatcher: null,
                recurseContainers: true);

            await aggregatingSigner.SignAsync(
                new[]
                {
                    SigningFile.Capture(firstApplication),
                    SigningFile.Capture(secondApplication)
                },
                options,
                coordinator);

            await mageCli.Received(2).RunAsync(Arg.Any<string>());
            Assert.True(deployFile.Exists);
            Assert.False(
                File.Exists(
                    Path.Combine(
                        deployFile.DirectoryName!,
                        Path.GetFileNameWithoutExtension(
                            deployFile.Name))));
            Assert.Equal(
                "unsigned-signed",
                File.ReadAllText(deployFile.FullName));
        }

        [Fact]
        public async Task SignAsync_DiscoveredDeploymentManifestOwnedByFlow_DoesNotRewriteLayout()
        {
            using TemporaryDirectory temporaryDirectory =
                new(_directoryService);
            await using SigningOperationCoordinator coordinator =
                new(_directoryService);
            FileInfo application = new(
                Path.Combine(
                    temporaryDirectory.Directory.FullName,
                    "App.application"));
            DirectoryInfo archiveDirectory = new(
                Path.Combine(
                    temporaryDirectory.Directory.FullName,
                    "Application Files",
                    "App_1_0_0_0"));
            FileInfo archivedApplication = new(
                Path.Combine(
                    archiveDirectory.FullName,
                    "App.application"));
            FileInfo marker = new(
                Path.Combine(
                    archiveDirectory.FullName,
                    "readme.txt"));

            archiveDirectory.Create();
            File.WriteAllText(application.FullName, string.Empty);
            File.WriteAllText(archivedApplication.FullName, string.Empty);
            File.WriteAllText(marker.FullName, "marker");

            using X509Certificate2 certificate =
                SelfIssuedCertificateCreator.CreateCertificate();
            using RSA privateKey =
                certificate.GetRSAPrivateKey()!;
            ISignatureAlgorithmProvider signatureAlgorithmProvider =
                Substitute.For<ISignatureAlgorithmProvider>();
            ICertificateProvider certificateProvider =
                Substitute.For<ICertificateProvider>();
            IServiceProvider serviceProvider =
                Substitute.For<IServiceProvider>();
            IMageCli mageCli = Substitute.For<IMageCli>();

            certificateProvider
                .GetCertificateAsync(Arg.Any<CancellationToken>())
                .Returns(certificate);
            signatureAlgorithmProvider
                .GetRsaAsync(Arg.Any<CancellationToken>())
                .Returns(privateKey);
            mageCli.RunAsync(Arg.Any<string>()).Returns(0);
            ClickOnceSigner clickOnceSigner = new(
                signatureAlgorithmProvider,
                certificateProvider,
                serviceProvider,
                mageCli,
                Substitute.For<IManifestSigner>(),
                Substitute.For<ILogger<IDataFormatSigner>>(),
                Substitute.For<IFileMatcher>());
            AggregatingSigner aggregatingSigner = new(
                new IDataFormatSigner[] { clickOnceSigner },
                Substitute.For<IDefaultDataFormatSigner>(),
                Substitute.For<IContainerProvider>(),
                Substitute.For<IFileMetadataService>(),
                Substitute.For<IMatcherFactory>());

            serviceProvider
                .GetService(typeof(IAggregatingDataFormatSigner))
                .Returns(aggregatingSigner);
            SignOptions options = new(
                applicationName: "ApplicationName",
                publisherName: "PublisherName",
                description: "Description",
                descriptionUrl:
                    new Uri("https://description.test"),
                fileHashAlgorithm: HashAlgorithmName.SHA256,
                timestampHashAlgorithm: HashAlgorithmName.SHA256,
                timestampService:
                    new Uri("http://timestamp.test"),
                matcher: null,
                antiMatcher: null,
                recurseContainers: true);

            // Snapshots can still read the marker, but replacing it fails.
            using (FileStream markerLock = new(
                marker.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                await aggregatingSigner.SignAsync(
                    new[] { SigningFile.Capture(application) },
                    options,
                    coordinator);
            }

            bool signedAgain = false;

            await coordinator.ExecuteArtifactAsync(
                SigningSourceIdentity.Capture(archivedApplication),
                () =>
                {
                    signedAgain = true;

                    return Task.FromResult(
                        SigningOperationArtifact.Single(
                            archivedApplication));
                });

            Assert.False(signedAgain);
            await mageCli.Received(2).RunAsync(Arg.Any<string>());
            Assert.Equal("marker", File.ReadAllText(marker.FullName));
        }

        [Fact]
        public async Task SignAsync_AncestorReusesCompletedDiscoveredLayout_RestoresDeploySuffix()
        {
            using TemporaryDirectory temporaryDirectory =
                new(_directoryService);
            await using SigningOperationCoordinator coordinator =
                new(_directoryService);
            FileInfo application = new(
                Path.Combine(
                    temporaryDirectory.Directory.FullName,
                    "App.application"));
            FileInfo nestedApplication = new(
                Path.Combine(
                    temporaryDirectory.Directory.FullName,
                    "nested",
                    "Nested.application"));
            DirectoryInfo archiveDirectory = new(
                Path.Combine(
                    nestedApplication.DirectoryName!,
                    "Application Files",
                    "Nested_1_0_0_0"));
            FileInfo archivedApplication = new(
                Path.Combine(
                    archiveDirectory.FullName,
                    "Nested.application"));
            FileInfo deployFile = new(
                Path.Combine(
                    archiveDirectory.FullName,
                    "payload.dll.deploy"));

            archiveDirectory.Create();
            File.WriteAllText(application.FullName, string.Empty);
            File.WriteAllText(nestedApplication.FullName, string.Empty);
            File.WriteAllText(archivedApplication.FullName, string.Empty);
            File.WriteAllText(deployFile.FullName, "unsigned");

            using X509Certificate2 certificate =
                SelfIssuedCertificateCreator.CreateCertificate();
            using RSA privateKey =
                certificate.GetRSAPrivateKey()!;
            ISignatureAlgorithmProvider signatureAlgorithmProvider =
                Substitute.For<ISignatureAlgorithmProvider>();
            ICertificateProvider certificateProvider =
                Substitute.For<ICertificateProvider>();
            IServiceProvider serviceProvider =
                Substitute.For<IServiceProvider>();
            IMageCli mageCli = Substitute.For<IMageCli>();
            IDataFormatSigner payloadSigner =
                Substitute.For<IDataFormatSigner>();

            certificateProvider
                .GetCertificateAsync(Arg.Any<CancellationToken>())
                .Returns(certificate);
            signatureAlgorithmProvider
                .GetRsaAsync(Arg.Any<CancellationToken>())
                .Returns(privateKey);
            mageCli.RunAsync(Arg.Any<string>()).Returns(0);
            payloadSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(
                    call =>
                        call.Arg<FileInfo>().Extension.Equals(
                            ".dll",
                            StringComparison.OrdinalIgnoreCase));
            payloadSigner
                .SignAsync(
                    Arg.Any<IEnumerable<FileInfo>>(),
                    Arg.Any<SignOptions>())
                .Returns(
                    call =>
                    {
                        foreach (FileInfo payload in
                            call.Arg<IEnumerable<FileInfo>>())
                        {
                            File.AppendAllText(
                                payload.FullName,
                                "-signed");
                        }

                        return Task.CompletedTask;
                    });
            ClickOnceSigner clickOnceSigner = new(
                signatureAlgorithmProvider,
                certificateProvider,
                serviceProvider,
                mageCli,
                Substitute.For<IManifestSigner>(),
                Substitute.For<ILogger<IDataFormatSigner>>(),
                Substitute.For<IFileMatcher>());
            AggregatingSigner aggregatingSigner = new(
                new IDataFormatSigner[]
                {
                    clickOnceSigner,
                    payloadSigner
                },
                Substitute.For<IDefaultDataFormatSigner>(),
                Substitute.For<IContainerProvider>(),
                Substitute.For<IFileMetadataService>(),
                Substitute.For<IMatcherFactory>());

            serviceProvider
                .GetService(typeof(IAggregatingDataFormatSigner))
                .Returns(aggregatingSigner);
            SignOptions options = new(
                applicationName: "ApplicationName",
                publisherName: "PublisherName",
                description: "Description",
                descriptionUrl:
                    new Uri("https://description.test"),
                fileHashAlgorithm: HashAlgorithmName.SHA256,
                timestampHashAlgorithm: HashAlgorithmName.SHA256,
                timestampService:
                    new Uri("http://timestamp.test"),
                matcher: null,
                antiMatcher: null,
                recurseContainers: true);

            // The nested input is signed first and owns the archived
            // manifest's layout, which the ancestor then reuses.
            await aggregatingSigner.SignAsync(
                new[] { SigningFile.Capture(nestedApplication) },
                options,
                coordinator);
            await aggregatingSigner.SignAsync(
                new[] { SigningFile.Capture(application) },
                options,
                coordinator);

            await mageCli.Received(3).RunAsync(Arg.Any<string>());
            Assert.True(deployFile.Exists);
            Assert.False(
                File.Exists(
                    Path.Combine(
                        deployFile.DirectoryName!,
                        Path.GetFileNameWithoutExtension(
                            deployFile.Name))));
            Assert.Equal(
                "unsigned-signed",
                File.ReadAllText(deployFile.FullName));
        }

        [Fact]
        public async Task SignAsync_ClickOnceInZip_PreservesContainerEntryIdentitiesAndReusesResults()
        {
            using TemporaryDirectory temporaryDirectory =
                new(_directoryService);
            await using SigningOperationCoordinator coordinator =
                new(_directoryService);
            FileInfo zipFile = new(
                Path.Combine(
                    temporaryDirectory.Directory.FullName,
                    "container.zip"));
            DirectoryInfo extractedDirectory = new(
                Path.Combine(
                    temporaryDirectory.Directory.FullName,
                    "extracted"));
            FileInfo firstApplication = new(
                Path.Combine(
                    extractedDirectory.FullName,
                    "First.application"));
            FileInfo secondApplication = new(
                Path.Combine(
                    extractedDirectory.FullName,
                    "Second.application"));
            FileInfo applicationManifest = new(
                Path.Combine(
                    extractedDirectory.FullName,
                    "MyApp.exe.manifest"));
            FileInfo deployFile = new(
                Path.Combine(
                    extractedDirectory.FullName,
                    "payload.dll.deploy"));
            SigningSourceIdentity zipIdentity =
                SigningSourceIdentity.Capture(zipFile);
            SigningSourceIdentity firstIdentity =
                SigningSourceIdentity.ContainerEntry(
                    zipIdentity,
                    "publish/First.application");
            SigningSourceIdentity secondIdentity =
                SigningSourceIdentity.ContainerEntry(
                    zipIdentity,
                    "publish/Second.application");
            SigningSourceIdentity manifestIdentity =
                SigningSourceIdentity.ContainerEntry(
                    zipIdentity,
                    "publish/MyApp.exe.manifest");
            SigningSourceIdentity deployIdentity =
                SigningSourceIdentity.ContainerEntry(
                    zipIdentity,
                    "publish/payload.dll.deploy");

            extractedDirectory.Create();
            File.WriteAllText(zipFile.FullName, "zip");
            File.WriteAllText(firstApplication.FullName, string.Empty);
            File.WriteAllText(secondApplication.FullName, string.Empty);
            File.WriteAllText(applicationManifest.FullName, string.Empty);
            File.WriteAllText(deployFile.FullName, "unsigned");

            using X509Certificate2 certificate =
                SelfIssuedCertificateCreator.CreateCertificate();
            using RSA privateKey =
                certificate.GetRSAPrivateKey()!;
            ISignatureAlgorithmProvider signatureAlgorithmProvider =
                Substitute.For<ISignatureAlgorithmProvider>();
            ICertificateProvider certificateProvider =
                Substitute.For<ICertificateProvider>();
            IServiceProvider serviceProvider =
                Substitute.For<IServiceProvider>();
            IMageCli mageCli = Substitute.For<IMageCli>();
            IDataFormatSigner payloadSigner =
                Substitute.For<IDataFormatSigner>();
            IContainer container = Substitute.For<IContainer>();
            IContainerProvider containerProvider =
                Substitute.For<IContainerProvider>();
            List<string> signedPayloads = new();

            certificateProvider
                .GetCertificateAsync(Arg.Any<CancellationToken>())
                .Returns(certificate);
            signatureAlgorithmProvider
                .GetRsaAsync(Arg.Any<CancellationToken>())
                .Returns(privateKey);
            mageCli.RunAsync(Arg.Any<string>()).Returns(0);
            payloadSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(
                    call =>
                        call.Arg<FileInfo>().Extension.Equals(
                            ".dll",
                            StringComparison.OrdinalIgnoreCase));
            payloadSigner
                .SignAsync(
                    Arg.Any<IEnumerable<FileInfo>>(),
                    Arg.Any<SignOptions>())
                .Returns(
                    call =>
                    {
                        foreach (FileInfo payload in
                            call.Arg<IEnumerable<FileInfo>>())
                        {
                            signedPayloads.Add(payload.Name);
                            File.AppendAllText(
                                payload.FullName,
                                "-signed");
                        }

                        return Task.CompletedTask;
                    });
            container
                .GetFiles(zipIdentity)
                .Returns(
                    new[]
                    {
                        new SigningFile(
                            firstApplication,
                            firstIdentity),
                        new SigningFile(
                            secondApplication,
                            secondIdentity),
                        new SigningFile(
                            applicationManifest,
                            manifestIdentity),
                        new SigningFile(
                            deployFile,
                            deployIdentity)
                    });
            containerProvider
                .IsZipContainer(zipFile)
                .Returns(true);
            containerProvider
                .GetContainer(zipFile)
                .Returns(container);
            ClickOnceSigner clickOnceSigner = new(
                signatureAlgorithmProvider,
                certificateProvider,
                serviceProvider,
                mageCli,
                Substitute.For<IManifestSigner>(),
                Substitute.For<ILogger<IDataFormatSigner>>(),
                Substitute.For<IFileMatcher>());
            AggregatingSigner aggregatingSigner = new(
                new IDataFormatSigner[]
                {
                    clickOnceSigner,
                    payloadSigner
                },
                Substitute.For<IDefaultDataFormatSigner>(),
                containerProvider,
                Substitute.For<IFileMetadataService>(),
                Substitute.For<IMatcherFactory>());

            serviceProvider
                .GetService(typeof(IAggregatingDataFormatSigner))
                .Returns(aggregatingSigner);
            SignOptions options = new(
                applicationName: "ApplicationName",
                publisherName: "PublisherName",
                description: "Description",
                descriptionUrl:
                    new Uri("https://description.test"),
                fileHashAlgorithm: HashAlgorithmName.SHA256,
                timestampHashAlgorithm: HashAlgorithmName.SHA256,
                timestampService:
                    new Uri("http://timestamp.test"),
                matcher: null,
                antiMatcher: null,
                recurseContainers: true);

            await aggregatingSigner.SignAsync(
                new[]
                {
                    new SigningFile(
                        zipFile,
                        zipIdentity)
                },
                options,
                coordinator);

            Assert.Equal(
                "payload.dll",
                Assert.Single(signedPayloads));

            bool payloadSignedAgain = false;

            // The payload was claimed under its container entry identity.
            await coordinator.ExecuteArtifactAsync(
                deployIdentity,
                () =>
                {
                    payloadSignedAgain = true;

                    return Task.FromResult(
                        SigningOperationArtifact.Single(deployFile));
                });

            Assert.False(payloadSignedAgain);
            await mageCli.Received(3).RunAsync(Arg.Any<string>());
            await container.Received(1).OpenAsync();
            await container.Received(1).SaveAsync();
            Assert.True(deployFile.Exists);
            Assert.Equal(
                "unsigned-signed",
                File.ReadAllText(deployFile.FullName));
        }

        [Fact]
        public async Task ExecuteAsync_ApplicationManifestInputBeforeDeploymentManifest_StillSignsApplicationManifest()
        {
            using TemporaryDirectory temporaryDirectory =
                new(_directoryService);
            await using SigningOperationCoordinator coordinator =
                new(_directoryService);
            FileInfo deploymentManifest = new(
                Path.Combine(
                    temporaryDirectory.Directory.FullName,
                    "App.application"));
            FileInfo applicationManifest = new(
                Path.Combine(
                    temporaryDirectory.Directory.FullName,
                    "App.exe.manifest"));

            File.WriteAllText(deploymentManifest.FullName, "deployment");
            File.WriteAllText(applicationManifest.FullName, "application");

            using X509Certificate2 certificate =
                SelfIssuedCertificateCreator.CreateCertificate();
            using RSA privateKey =
                certificate.GetRSAPrivateKey()!;
            ISignatureAlgorithmProvider signatureAlgorithmProvider =
                Substitute.For<ISignatureAlgorithmProvider>();
            ICertificateProvider certificateProvider =
                Substitute.For<ICertificateProvider>();
            IServiceProvider serviceProvider =
                Substitute.For<IServiceProvider>();
            IMageCli mageCli = Substitute.For<IMageCli>();
            List<string?> mageArguments = new();

            certificateProvider
                .GetCertificateAsync(Arg.Any<CancellationToken>())
                .Returns(certificate);
            signatureAlgorithmProvider
                .GetRsaAsync(Arg.Any<CancellationToken>())
                .Returns(privateKey);
            mageCli
                .RunAsync(Arg.Any<string>())
                .Returns(
                    call =>
                    {
                        lock (mageArguments)
                        {
                            mageArguments.Add(call.Arg<string>());
                        }

                        return 0;
                    });
            ClickOnceSigner clickOnceSigner = new(
                signatureAlgorithmProvider,
                certificateProvider,
                serviceProvider,
                mageCli,
                Substitute.For<IManifestSigner>(),
                Substitute.For<ILogger<IDataFormatSigner>>(),
                Substitute.For<IFileMatcher>());
            AggregatingSigner aggregatingSigner = new(
                new IDataFormatSigner[] { clickOnceSigner },
                Substitute.For<IDefaultDataFormatSigner>(),
                Substitute.For<IContainerProvider>(),
                Substitute.For<IFileMetadataService>(),
                Substitute.For<IMatcherFactory>());

            serviceProvider
                .GetService(typeof(IAggregatingDataFormatSigner))
                .Returns(aggregatingSigner);
            SignOptions options = new(
                HashAlgorithmName.SHA256,
                new Uri("http://timestamp.test"));
            SigningOperationExecutor executor = new(
                aggregatingSigner,
                _directoryService,
                Substitute.For<ILogger<ISigner>>(),
                coordinator);
            IReadOnlyList<SigningOperationPlan> plans =
                SigningOperationPlanner.Create(
                    new[] { deploymentManifest, applicationManifest },
                    output: null,
                    temporaryDirectory.Directory);

            // Signer runs every non-ClickOnce input before ClickOnce inputs.
            await executor.ExecuteAsync(plans[1], options);
            await executor.ExecuteAsync(plans[0], options);

            // Only the application-manifest update is followed by " -a"; the
            // deployment-manifest update references it through "-appm".
            Assert.Contains(
                mageArguments,
                arguments => arguments!.Contains(
                    $@"{applicationManifest.Name}"" -a",
                    StringComparison.OrdinalIgnoreCase));
            Assert.Equal(expected: 2, actual: mageArguments.Count);
        }

        [Fact]
        public void StageSigningDependencies_CopiesCorrectFiles()
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

                using (X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate())
                using (RSA privateKey = certificate.GetRSAPrivateKey()!)
                {
                    ISignatureAlgorithmProvider signatureAlgorithmProvider = Substitute.For<ISignatureAlgorithmProvider>();
                    ICertificateProvider certificateProvider = Substitute.For<ICertificateProvider>();
                    certificateProvider.GetCertificateAsync(Arg.Any<CancellationToken>()).Returns(certificate);
                    signatureAlgorithmProvider.GetRsaAsync(Arg.Any<CancellationToken>()).Returns(privateKey);

                    IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
                    AggregatingSignerSpy aggregatingSignerSpy = new();
                    serviceProvider.GetService(Arg.Any<Type>()).Returns(aggregatingSignerSpy);

                    IMageCli mageCli = Substitute.For<IMageCli>();
                    string publisher = certificate.SubjectName.Name;

                    IManifestSigner manifestSigner = Substitute.For<IManifestSigner>();
                    IFileMatcher fileMatcher = Substitute.For<IFileMatcher>();

                    SignOptions options = new(
                        "ApplicationName",
                        "PublisherName",
                        "Description",
                        new Uri("https://description.test"),
                        HashAlgorithmName.SHA256,
                        HashAlgorithmName.SHA256,
                        new Uri("http://timestamp.test"),
                        matcher: null,
                        antiMatcher: null,
                        recurseContainers: true
                    );

                    ILogger<IDataFormatSigner> logger = Substitute.For<ILogger<IDataFormatSigner>>();
                    ClickOnceSigner signer = new(
                        signatureAlgorithmProvider,
                        certificateProvider,
                        serviceProvider,
                        mageCli,
                        manifestSigner,
                        logger,
                        fileMatcher);

                    using (TemporaryDirectory signingDirectory = new(_directoryService))
                    {
                        // ensure that we start with nothing
                        Assert.Empty(signingDirectory.Directory.EnumerateFiles());
                        Assert.Empty(signingDirectory.Directory.EnumerateDirectories());
                        // tell the provider to copy what it needs into the signing directory
                        signer.StageSigningDependencies(
                            applicationFile,
                            signingDirectory.Directory,
                            options);
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
    }
}
