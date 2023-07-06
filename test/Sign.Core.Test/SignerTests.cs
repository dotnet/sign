// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using Moq;
using NuGet.Packaging;

namespace Sign.Core.Test
{
    [Collection(SigningTestsCollection.Name)]
    public sealed class SignerTests : IDisposable
    {
        private readonly CertificatesFixture _certificatesFixture;
        private readonly KeyVaultServiceStub _keyVaultServiceStub;

        public SignerTests(CertificatesFixture certificatesFixture)
        {
            ArgumentNullException.ThrowIfNull(certificatesFixture, nameof(certificatesFixture));

            _certificatesFixture = certificatesFixture;
            _keyVaultServiceStub = new KeyVaultServiceStub();
        }

        public void Dispose()
        {
            _keyVaultServiceStub.Dispose();
        }

        [Fact]
        public void Constructor_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new Signer(serviceProvider: null!, Mock.Of<ILogger<ISigner>>()));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new Signer(Mock.Of<IServiceProvider>(), logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenFileIsPortableExecutable_Signs()
        {
            using (TemporaryDirectory temporaryDirectory = new(new DirectoryService(Mock.Of<ILogger<IDirectoryService>>())))
            {
                FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);
                FileInfo file = new(Path.Combine(temporaryDirectory.Directory.FullName, thisAssemblyFile.Name));

                File.Copy(thisAssemblyFile.FullName, file.FullName);

                FileInfo outputFile = new(Path.Combine(temporaryDirectory.Directory.FullName, "signed.dll"));

                await SignAsync(temporaryDirectory, file, outputFile);

                await VerifyAuthenticodeSignedFileAsync(outputFile);
            }
        }

        [Fact]
        public async Task SignAsync_WhenFileIsPowerShellScript_Signs()
        {
            using (TemporaryDirectory temporaryDirectory = new(new DirectoryService(Mock.Of<ILogger<IDirectoryService>>())))
            {
                FileInfo file = new(Path.Combine(temporaryDirectory.Directory.FullName, "script.ps1"));

                File.WriteAllText(file.FullName, "Write-Host 'Hello, World!'");

                FileInfo outputFile = new(Path.Combine(temporaryDirectory.Directory.FullName, "signed.ps1"));

                await SignAsync(temporaryDirectory, file, outputFile);

                SignedCms signedCms = GetSignedCmsFromPowerShellScript(outputFile);

                await VerifySignedCmsAsync(signedCms);
            }
        }

        [Fact]
        public async Task SignAsync_WhenFileIsVsix_Signs()
        {
            using (TemporaryDirectory temporaryDirectory = new(new DirectoryService(Mock.Of<ILogger<IDirectoryService>>())))
            {
                FileInfo file = GetTestAsset(temporaryDirectory, "VsixPackage.vsix");
                FileInfo outputFile = new(Path.Combine(temporaryDirectory.Directory.FullName, "signed.vsix"));

                await SignAsync(temporaryDirectory, file, outputFile);

                await VerifyVsixAsync(outputFile, temporaryDirectory);
            }
        }

        [Fact]
        public async Task SignAsync_WhenFileIsMsixBundle_Signs()
        {
            using (TemporaryDirectory temporaryDirectory = new(new DirectoryService(Mock.Of<ILogger<IDirectoryService>>())))
            {
                FileInfo file = GetTestAsset(temporaryDirectory, "App1_1.0.0.0_x64.msixbundle");
                FileInfo outputFile = new(Path.Combine(temporaryDirectory.Directory.FullName, "signed.msixbundle"));

                await SignAsync(temporaryDirectory, file, outputFile);

                await VerifyMsixBundleFile(outputFile, temporaryDirectory);
            }
        }

        private async Task SignAsync(TemporaryDirectory temporaryDirectory, FileInfo file, FileInfo outputFile)
        {
            ServiceProvider serviceProvider = Create();
            TestLogger<ISigner> logger = new();
            Signer signer = new(serviceProvider, logger);

            int exitCode = await signer.SignAsync(
                new[] { file },
                outputFile: outputFile.FullName,
                fileList: null,
                temporaryDirectory.Directory,
                applicationName: "a",
                publisherName: null,
                description: "b",
                new Uri("https://description.test"),
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 4,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256,
                new DefaultAzureCredential(),
                new Uri("https://keyvault.test"),
                certificateName: "c");

            Assert.Equal(ExitCode.Success, exitCode);

            TestLogEntry lastLogEntry = logger.Entries.Last();

            Assert.Equal(LogLevel.Information, lastLogEntry.LogLevel);
            Assert.Matches(@"^Completed in \d+ ms.$", lastLogEntry.Message);
        }

        private static FileInfo GetTestAsset(TemporaryDirectory temporaryDirectory, string fileName)
        {
            FileInfo thisAssemblyFile = new(Path.Combine(Assembly.GetExecutingAssembly().Location));
            FileInfo testAssetFile = new(Path.Combine(thisAssemblyFile.DirectoryName!, "TestAssets", fileName));
            FileInfo testAssetFileCopy = new(Path.Combine(temporaryDirectory.Directory.FullName, fileName));

            File.Copy(testAssetFile.FullName, testAssetFileCopy.FullName);

            return testAssetFileCopy;
        }

        private async Task VerifyAuthenticodeSignedFileAsync(FileInfo outputFile)
        {
            Assert.True(AuthenticodeSignatureReader.TryGetSignedCms(outputFile, out SignedCms? signedCms));

            await VerifySignedCmsAsync(signedCms);
        }

        private async Task VerifyMsixBundleFile(FileInfo outputFile, TemporaryDirectory temporaryDirectory)
        {
            using (FileStream fileStream = outputFile.OpenRead())
            using (ZipArchive msixBundle = new(fileStream))
            {
                await VerifyAppxSignatureAsync(msixBundle);

                ZipArchiveEntry? entry = msixBundle.GetEntry("App1_1.0.0.0_x64.msix");

                Assert.NotNull(entry);

                using (Stream msixStream = entry.Open())
                using (ZipArchive msix = new(msixStream))
                {
                    await VerifyAppxSignatureAsync(msix);

                    foreach (string entryPath in new[]
                    {
                        "AppxMetadata/CodeIntegrity.cat",
                        "App1.dll",
                        "App1.exe",
                        "clrcompression.dll",
                    })
                    {
                        entry = msix.GetEntry(entryPath);

                        Assert.NotNull(entry);

                        FileInfo extractedFile = ExtractEntry(temporaryDirectory, entry);

                        await VerifyAuthenticodeSignedFileAsync(extractedFile);
                    }
                }
            }
        }

        private async Task VerifyVsixAsync(FileInfo outputFile, TemporaryDirectory temporaryDirectory)
        {
            using (FileStream fileStream = outputFile.OpenRead())
            using (ZipArchive vsix = new(fileStream))
            {
                ZipArchiveEntry? dllEntry = vsix.GetEntry("VsixPackage.dll");

                Assert.NotNull(dllEntry);

                FileInfo extractedFile = ExtractEntry(temporaryDirectory, dllEntry);

                await VerifyAuthenticodeSignedFileAsync(extractedFile);

                Assert.True(TryGetSignatureEntry(vsix, out ZipArchiveEntry? signatureEntry));

                extractedFile = ExtractEntry(temporaryDirectory, signatureEntry);

                await VerifyXmlDsigAsync(extractedFile);
            }
        }

        private static FileInfo ExtractEntry(TemporaryDirectory temporaryDirectory, ZipArchiveEntry entry)
        {
            FileInfo file = new(Path.Combine(temporaryDirectory.Directory.FullName, entry.Name));

            using (Stream stream = entry.Open())
            {
                stream.CopyToFile(file.FullName);
            }

            return file;
        }

        private async Task VerifyAppxSignatureAsync(ZipArchive msix)
        {
            ZipArchiveEntry? entry = msix.GetEntry("AppxSignature.p7x");

            Assert.NotNull(entry);

            SignedCms signedCms = GetSignedCms(entry);

            await VerifySignedCmsAsync(signedCms);
        }

        private async Task VerifyXmlDsigAsync(FileInfo extractedFile)
        {
            XmlDocument xmlDoc = new()
            {
                PreserveWhitespace = true
            };
            xmlDoc.Load(extractedFile.FullName);

            SignedXml signedXml = new(xmlDoc);
            XmlNodeList nodes = xmlDoc.GetElementsByTagName("Signature");
            XmlElement? node = Assert.Single(nodes) as XmlElement;

            Assert.NotNull(node);

            signedXml.LoadXml(node);

            using (X509Certificate2 expectedCertificate = await _keyVaultServiceStub.GetCertificateAsync())
            {
                Assert.True(signedXml.CheckSignature(expectedCertificate, verifySignatureOnly: true));
            }

            nodes = xmlDoc.GetElementsByTagName("EncodedTime");
            node = Assert.Single(nodes) as XmlElement;

            Assert.NotNull(node);

            SignedCms signedCms = GetSignedCmsFromBase64(node.InnerText);

            VerifyTimestampSignedCms(signedCms);
        }

        private static SignedCms GetSignedCms(ZipArchiveEntry entry)
        {
            Memory<byte> buffer = new byte[entry.Length];

            using (Stream stream = entry!.Open())
            {
                stream.Read(buffer.Span);
            }

            SignedCms signedCms = new();

            // The first 4 bytes are 0x504B4358 ("PKCX").
            signedCms.Decode(buffer[4..].Span);

            return signedCms;
        }

        private static SignedCms GetSignedCmsFromBase64(string base64)
        {
            byte[] bytes = Convert.FromBase64String(base64);
            SignedCms signedCms = new();

            signedCms.Decode(bytes);

            return signedCms;
        }

        private static SignedCms GetSignedCmsFromPowerShellScript(FileInfo file)
        {
            StringBuilder base64 = new();

            using (FileStream stream = file.OpenRead())
            using (StreamReader reader = new(stream))
            {
                string? line;

                while ((line = reader.ReadLine()) is not null)
                {
                    if (!line.StartsWith("#"))
                    {
                        continue;
                    }

                    line = line.Trim('#', ' ');

                    if (line.StartsWith("SIG #"))
                    {
                        continue;
                    }

                    base64.Append(line);
                }
            }

            return GetSignedCmsFromBase64(base64.ToString());
        }

        private static bool TryGetSignatureEntry(ZipArchive zipArchive, [NotNullWhen(true)] out ZipArchiveEntry? signatureEntry)
        {
            signatureEntry = null;

            foreach (ZipArchiveEntry entry in zipArchive.Entries)
            {
                if (entry.FullName.StartsWith("package/services/digital-signature/xml-signature/"))
                {
                    signatureEntry = entry;

                    break;
                }
            }

            return signatureEntry is not null;
        }

        private async Task VerifySignedCmsAsync(SignedCms signedCms)
        {
            SignerInfo signerInfo = signedCms.SignerInfos[0];

            using (X509Certificate2 expectedCertificate = await _keyVaultServiceStub.GetCertificateAsync())
            {
                Assert.True(expectedCertificate.Equals(signerInfo.Certificate));
            }

            signerInfo.CheckSignature(verifySignatureOnly: true);

            Assert.True(TryGetTimestampSignedCms(signerInfo, out SignedCms? timestampSignedCms));

            VerifyTimestampSignedCms(timestampSignedCms);
        }

        private void VerifyTimestampSignedCms(SignedCms timestampSignedCms)
        {
            SignerInfo timestampSignerInfo = timestampSignedCms.SignerInfos[0];

            Assert.True(_certificatesFixture.TimestampServiceCertificate.Equals(timestampSignerInfo.Certificate));

            timestampSignerInfo.CheckSignature(verifySignatureOnly: true);
        }

        private static bool TryGetTimestampSignedCms(SignerInfo signerInfo, [NotNullWhen(true)] out SignedCms? timestampSignedCms)
        {
            timestampSignedCms = null;

            CryptographicAttributeObjectCollection unsignedAttributes = signerInfo.UnsignedAttributes;

            foreach (CryptographicAttributeObject attribute in unsignedAttributes)
            {
                if (attribute.Oid.IsEqualTo(Oids.MicrosoftRfc3161Timestamp))
                {
                    foreach (AsnEncodedData value in attribute.Values)
                    {
                        SignedCms signedCms = new();

                        signedCms.Decode(value.RawData);

                        timestampSignedCms = signedCms;

                        break;
                    }
                }
            }

            return timestampSignedCms is not null;
        }

        private ServiceProvider Create()
        {
            ServiceCollection services = new();

            services.AddLogging();

            services.AddSingleton<IAppRootDirectoryLocator, AppRootDirectoryLocator>();
            services.AddSingleton<IToolConfigurationProvider, ToolConfigurationProvider>();
            services.AddSingleton<IMatcherFactory, MatcherFactory>();
            services.AddSingleton<IFileListReader, FileListReader>();
            services.AddSingleton<IFileMatcher, FileMatcher>();
            services.AddSingleton<IContainerProvider, ContainerProvider>();
            services.AddSingleton<IFileMetadataService, FileMetadataService>();
            services.AddSingleton<IDirectoryService, DirectoryService>();
            services.AddSingleton<IKeyVaultService>(_keyVaultServiceStub);
            services.AddSingleton<ISignatureProvider, AzureSignToolSignatureProvider>();
            services.AddSingleton<ISignatureProvider, ClickOnceSignatureProvider>();
            services.AddSingleton<ISignatureProvider, VsixSignatureProvider>();
            services.AddSingleton<ISignatureProvider, NuGetSignatureProvider>();
            services.AddSingleton<ISignatureProvider, AppInstallerServiceSignatureProvider>();
            services.AddSingleton<IDefaultSignatureProvider, DefaultSignatureProvider>();
            services.AddSingleton<IAggregatingSignatureProvider, AggregatingSignatureProvider>();
            services.AddSingleton<IManifestSigner, ManifestSigner>();
            services.AddSingleton<IMageCli, MageCli>();
            services.AddSingleton<IMakeAppxCli, MakeAppxCli>();
            services.AddSingleton<INuGetSignTool, NuGetSignTool>();
            services.AddSingleton<IOpenVsixSignTool, OpenVsixSignTool>();
            services.AddSingleton<ICertificateVerifier, CertificateVerifier>();
            services.AddSingleton<ISigner, Signer>();

            return new ServiceProvider(services.BuildServiceProvider());
        }
    }
}