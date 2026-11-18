// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    [Collection(SigningTestsCollection.Name)]
    public sealed class PackagedToolTests : IDisposable
    {
        private readonly CertificatesFixture _certificatesFixture;
        private readonly DirectoryService _directoryService = new(Substitute.For<ILogger<IDirectoryService>>());
        private readonly TemporaryDirectory _directory;
        private readonly X509Certificate2 _certificate = SelfIssuedCertificateCreator.CreateCertificate();
        private readonly string _certificatePath;

        public PackagedToolTests(CertificatesFixture certificatesFixture)
        {
            _certificatesFixture = certificatesFixture;
            _directory = new(_directoryService);
            _certificatePath = Path.Combine(_directory.Directory.FullName, "test.pfx");
            File.WriteAllBytes(_certificatePath, _certificate.Export(X509ContentType.Pfx));
        }

        [PackagedToolFact]
        public void ProcessArchitecture_WhenToolIsInstalled_MatchesExpectedArchitecture()
        {
            Assert.True(Enum.TryParse(Environment.GetEnvironmentVariable("SIGN_TEST_EXPECTED_ARCHITECTURE"),
                ignoreCase: true, out Architecture expected));
            Assert.Equal(expected, RuntimeInformation.ProcessArchitecture);

            Machine expectedMachine = expected switch
            {
                Architecture.X64 => Machine.Amd64,
                Architecture.Arm64 => Machine.Arm64,
                _ => throw new PlatformNotSupportedException()
            };

            using (FileStream stream = File.OpenRead(Environment.GetEnvironmentVariable("SIGN_TEST_TOOL_PATH")!))
            using (PEReader reader = new(stream))
            {
                Assert.Equal(expectedMachine, reader.PEHeaders.CoffHeader.Machine);
            }
        }

        [PackagedToolFact]
        public async Task SignAsync_WhenFileIsPortableExecutable_SignsAndTimestamps()
        {
            FileInfo file = new(Path.Combine(_directory.Directory.FullName, "unsigned.dll"));
            File.Copy(typeof(PackagedToolTests).Assembly.Location, file.FullName);
            FileInfo signedFile = new(Path.Combine(_directory.Directory.FullName, "signed.dll"));

            (int exitCode, string output) = await SignAsync(file, signedFile);

            Assert.True(exitCode == 0, output);
            Assert.True(AuthenticodeSignatureReader.TryGetSignedCms(signedFile, out SignedCms? signedCms));
            SignerInfo signer = Assert.Single(signedCms.SignerInfos.Cast<SignerInfo>());
            Assert.Equal(_certificate, signer.Certificate);
            signer.CheckSignature(verifySignatureOnly: true);

            CryptographicAttributeObject timestamp = Assert.Single(
                signer.UnsignedAttributes.Cast<CryptographicAttributeObject>(),
                attribute => attribute.Oid.IsEqualTo(Oids.MicrosoftRfc3161Timestamp));
            SignedCms timestampCms = new();
            timestampCms.Decode(Assert.Single(timestamp.Values.Cast<AsnEncodedData>()).RawData);
            SignerInfo timestampSigner = Assert.Single(timestampCms.SignerInfos.Cast<SignerInfo>());
            Assert.True(_certificatesFixture.TimestampServiceCertificate.Equals(timestampSigner.Certificate));
            timestampSigner.CheckSignature(verifySignatureOnly: true);
        }

        [PackagedToolFact(arm64Only: true)]
        public async Task SignAsync_WhenFileIsBusinessCentralAppOnArm64_ReportsUnsupportedPlatform()
        {
            FileInfo file = TestAssets.GetTestAsset(_directory.Directory, "EmptyExtension.app");
            FileInfo signedFile = new(Path.Combine(_directory.Directory.FullName, "signed.app"));

            (int exitCode, string output) = await SignAsync(file, signedFile);

            Assert.NotEqual(0, exitCode);
            string? expectedMessage = Resources.ResourceManager.GetString("DynamicsBusinessCentralArm64NotSupported");
            Assert.NotNull(expectedMessage);
            Assert.Contains(expectedMessage, output);
        }

        private async Task<(int ExitCode, string Output)> SignAsync(FileInfo file, FileInfo output)
        {
            ProcessStartInfo startInfo = new(Environment.GetEnvironmentVariable("SIGN_TEST_TOOL_PATH")!)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _directory.Directory.FullName
            };

            foreach (string argument in new[]
            {
                "code", "certificate-store",
                "--certificate-file", _certificatePath,
                "--certificate-fingerprint", _certificate.GetCertHashString(HashAlgorithmName.SHA256),
                "--timestamp-url", _certificatesFixture.TimestampServiceUrl.AbsoluteUri,
                "--output", output.FullName,
                file.FullName
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using (Process process = new() { StartInfo = startInfo })
            using (CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2)))
            {
                Assert.True(process.Start());
                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    throw;
                }

                return (process.ExitCode, await standardOutput + await standardError);
            }
        }

        public void Dispose()
        {
            _certificate.Dispose();
            _directory.Dispose();
            _directoryService.Dispose();
        }
    }

    internal sealed class PackagedToolFactAttribute : FactAttribute
    {
        private readonly bool _arm64Only;

        public PackagedToolFactAttribute(bool arm64Only = false)
        {
            _arm64Only = arm64Only;
        }

        public override string Skip
        {
            get
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SIGN_TEST_TOOL_PATH")))
                {
                    return "Requires an installed tool package supplied by architecture validation.";
                }

                if (_arm64Only && RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
                {
                    return "Requires an ARM64 process.";
                }

                return base.Skip;
            }
            set { base.Skip = value; }
        }
    }
}
