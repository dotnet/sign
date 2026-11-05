// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.CommandLine;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sign.Core;
using Sign.TestInfrastructure;

namespace Sign.Cli.Test
{
    public sealed class ClickOnceDispatchTests
    {
        [Theory]
        [InlineData("", ".application")]
        [InlineData("--clickonce-signing-version 1", ".application")]
        [InlineData("--clickonce-signing-version 2", ".application")]
        [InlineData("", ".vsto")]
        [InlineData("--clickonce-signing-version 1", ".vsto")]
        [InlineData("--clickonce-signing-version 2", ".vsto")]
        public async Task Cli_SelectsOneAlgorithmAndSignsEachManifestExactlyOnce(string options, string extension)
        {
            using Harness harness = new();
            FileInfo application = harness.Application("App.manifest");
            FileInfo deployment = harness.Deployment($"App{extension}", application.Name);

            int exitCode = await harness.InvokeAsync(options, deployment.Name);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(new[] { ".manifest", extension }, harness.ManifestCalls.Select(file => file.Extension));
            Assert.Equal(options.EndsWith('2') ? 0 : 2, harness.MageCalls.Count);
            Assert.DoesNotContain(harness.Logger.Entries,
                entry => entry.LogLevel == LogLevel.Warning);
            Assert.Empty(harness.PayloadCalls);
        }

        [Theory]
        [InlineData("")]
        [InlineData("--clickonce-signing-version 1")]
        [InlineData("--clickonce-signing-version 2")]
        public async Task Cli_ExplicitApplicationManifestIsOnlyClaimedByVersion2(string options)
        {
            using Harness harness = new();
            FileInfo application = harness.Application("App.manifest");
            string original = File.ReadAllText(application.FullName);

            int exitCode = await harness.InvokeAsync(options, application.Name);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Empty(harness.MageCalls);
            if (options.EndsWith('2'))
            {
                Assert.Single(harness.ManifestCalls);
            }
            else
            {
                Assert.Empty(harness.ManifestCalls);
                Assert.Equal(original, File.ReadAllText(application.FullName));
            }
            Assert.DoesNotContain(harness.Logger.Entries, entry => entry.LogLevel == LogLevel.Warning);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task Cli_UpdateOnlyOrdersExplicitApplicationBeforeDeployment(
            bool deploymentFirst, bool outOfPlace)
        {
            using Harness harness = new();
            harness.File("payload.dll.deploy", "payload");
            FileInfo application = harness.Application(@"v1\App.manifest", "payload.dll");
            harness.File(@"v1\payload.dll.deploy", "payload");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest", mapped: true);
            string output = outOfPlace ? $"--output \"{Path.Combine(harness.Source.FullName, "output")}\"" : "";
            string[] inputs = deploymentFirst
                ? new[] { deployment.Name, @"v1\App.manifest" }
                : new[] { @"v1\App.manifest", deployment.Name };

            int exitCode = await harness.InvokeAsync(
                $"--clickonce-signing-version 2 --no-sign-clickonce-deps {output}", inputs);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(new[] { ".manifest", ".application" }, harness.ManifestCalls.Select(file => file.Extension));
            Assert.Empty(harness.PayloadCalls);
            Assert.Empty(harness.MageCalls);
            DirectoryInfo root = outOfPlace
                ? new DirectoryInfo(Path.Combine(harness.Source.FullName, "output"))
                : harness.Source;
            harness.AssertDeploymentHash(
                new FileInfo(Path.Combine(root.FullName, "App.application")),
                new FileInfo(Path.Combine(root.FullName, @"v1\App.manifest")));
            Assert.Equal("payload", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, @"v1\payload.dll.deploy")));
            if (outOfPlace)
            {
                Assert.False(System.IO.File.Exists(Path.Combine(root.FullName, @"v1\payload.dll.deploy")));
            }
        }

        [Theory]
        [InlineData("--no-sign-clickonce-deps")]
        [InlineData("--no-update-clickonce-manifest")]
        public async Task Cli_DependencyModesDoNotSignImplicitApplicationOrPayload(string option)
        {
            using Harness harness = new();
            FileInfo application = harness.Application("App.manifest", "missing.dll");
            FileInfo deployment = harness.Deployment("App.application", application.Name);
            string originalApplication = System.IO.File.ReadAllText(application.FullName);
            string originalDeployment = System.IO.File.ReadAllText(deployment.FullName);

            int exitCode = await harness.InvokeAsync($"--clickonce-signing-version 2 {option}", deployment.Name);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(".application", Assert.Single(harness.ManifestCalls).Extension);
            Assert.Empty(harness.PayloadCalls);
            Assert.Equal(originalApplication, System.IO.File.ReadAllText(application.FullName));
            if (option == "--no-update-clickonce-manifest")
            {
                Assert.Equal(originalDeployment + "\n<!--signed-->", System.IO.File.ReadAllText(deployment.FullName));
            }
            else
            {
                harness.AssertDeploymentHash(deployment, application);
            }
        }

        [Fact]
        public async Task Cli_SignOnlyRequiresNoReferencedFilesAndPreservesManifestMetadata()
        {
            using Harness harness = new();
            FileInfo deployment = harness.Deployment("App.vsto", "missing.manifest");
            FileInfo application = harness.Application("Other.manifest", "missing.dll");
            string before = System.IO.File.ReadAllText(application.FullName);

            int exitCode = await harness.InvokeAsync(
                "--clickonce-signing-version 2 --no-update-clickonce-manifest", deployment.Name, application.Name);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(2, harness.ManifestCalls.Count);
            Assert.Empty(harness.PayloadCalls);
            Assert.Empty(harness.MageCalls);
            Assert.Equal(before + "\n<!--signed-->", System.IO.File.ReadAllText(application.FullName));
        }

        [Fact]
        public async Task Cli_FullPipelineFollowsSelectedVersionAndKeepsNormalUserMatchingSeparate()
        {
            using Harness harness = new();
            harness.File(@"v1\payload.dll.deploy", "selected");
            harness.File(@"v1\data.txt.deploy", "excluded");
            harness.File(@"v2\old.dll", "old");
            harness.File("unrelated.dll", "unrelated");
            harness.File("setup.exe", "setup");
            harness.Application(@"v1\App.manifest", "payload.dll", "data.txt");
            harness.Application(@"v2\Old.manifest", "old.dll");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest", mapped: true);

            int exitCode = await harness.InvokeAsync(
                "--clickonce-signing-version 2", deployment.Name, "unrelated.dll");

            Assert.True(exitCode == ExitCode.Success,
                string.Join(Environment.NewLine, harness.SigningLogger.Entries.Select(entry => entry.Message)));
            Assert.Equal(3, harness.PayloadCalls.Count);
            Assert.Equal(2, harness.PayloadCalls.Count(file => file.Extension == ".dll"));
            Assert.Single(harness.PayloadCalls, file => file.Extension == ".exe");
            Assert.Equal("setup signed", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, "setup.exe")));
            Assert.Equal("unrelated signed", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, "unrelated.dll")));
            Assert.Equal("old", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, @"v2\old.dll")));
            Assert.Equal("excluded", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, @"v1\data.txt.deploy")));
            Assert.Equal("selected signed", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, @"v1\payload.dll.deploy")));
            Assert.Equal(2, harness.ManifestCalls.Count);
            harness.AssertDeploymentHash(deployment, new FileInfo(Path.Combine(harness.Source.FullName, @"v1\App.manifest")));
        }

        [Fact]
        public async Task Cli_ExplicitPayloadAndApplicationInputsDoNotDoubleSignImplicitGraph()
        {
            using Harness harness = new();
            harness.File(@"v1\payload.dll", "payload");
            harness.Application(@"v1\App.manifest", "payload.dll");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest");

            int exitCode = await harness.InvokeAsync("--clickonce-signing-version 2",
                deployment.Name, @"v1\payload.dll", @"v1\App.manifest");

            Assert.True(exitCode == ExitCode.Success,
                string.Join(Environment.NewLine, harness.SigningLogger.Entries.Concat(harness.Logger.Entries).Select(entry => entry.Message)));
            Assert.Single(harness.PayloadCalls);
            Assert.Equal(2, harness.ManifestCalls.Count);
            Assert.Equal("payload signed", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, @"v1\payload.dll")));
            harness.AssertDeploymentHash(deployment, new FileInfo(Path.Combine(harness.Source.FullName, @"v1\App.manifest")));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Cli_DeploymentDirectoryFallbackPreservesPublishedPayloadLocation(bool outOfPlace)
        {
            using Harness harness = new();
            harness.File("payload.dll.deploy", "fallback");
            harness.Application(@"v1\App.manifest", "payload.dll");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest", mapped: true);
            string output = outOfPlace ? $"--output \"{Path.Combine(harness.Source.FullName, "output")}\"" : "";

            int exitCode = await harness.InvokeAsync($"--clickonce-signing-version 2 {output}", deployment.Name);

            Assert.True(exitCode == ExitCode.Success,
                string.Join(Environment.NewLine, harness.SigningLogger.Entries.Select(entry => entry.Message)));
            Assert.Single(harness.PayloadCalls);
            DirectoryInfo root = outOfPlace
                ? new DirectoryInfo(Path.Combine(harness.Source.FullName, "output"))
                : harness.Source;
            Assert.Equal("fallback signed", System.IO.File.ReadAllText(Path.Combine(root.FullName, "payload.dll.deploy")));
            Assert.False(System.IO.File.Exists(Path.Combine(root.FullName, @"v1\payload.dll.deploy")));
            harness.AssertDeploymentHash(new FileInfo(Path.Combine(root.FullName, deployment.Name)),
                new FileInfo(Path.Combine(root.FullName, @"v1\App.manifest")));
            if (outOfPlace)
            {
                Assert.Equal("fallback", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, "payload.dll.deploy")));
            }
        }

        [Fact]
        public async Task Cli_FullPipelineOutOfPlacePublishesOnlySelectedSignedFiles()
        {
            using Harness harness = new();
            harness.File(@"v1\payload.dll.deploy", "selected");
            harness.File(@"v1\excluded.dll.deploy", "excluded");
            harness.File(@"v1\data.txt.deploy", "data");
            harness.File("unrelated.exe", "unrelated");
            harness.File("files.txt", "**/*\n!**/excluded.dll.deploy");
            harness.Application(@"v1\App.manifest", "payload.dll", "excluded.dll", "data.txt");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest", mapped: true);
            string output = Path.Combine(harness.Source.FullName, "output");

            int exitCode = await harness.InvokeAsync(
                $"--clickonce-signing-version 2 --file-list files.txt --output \"{output}\"", deployment.Name);

            Assert.True(exitCode == ExitCode.Success,
                string.Join(Environment.NewLine, harness.SigningLogger.Entries.Select(entry => entry.Message)));
            Assert.Single(harness.PayloadCalls);
            Assert.Equal(3, Directory.GetFiles(output, "*", SearchOption.AllDirectories).Length);
            Assert.Equal("selected signed", System.IO.File.ReadAllText(Path.Combine(output, @"v1\payload.dll.deploy")));
            Assert.Equal("selected", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, @"v1\payload.dll.deploy")));
            Assert.False(System.IO.File.Exists(Path.Combine(output, @"v1\excluded.dll.deploy")));
            Assert.False(System.IO.File.Exists(Path.Combine(output, @"v1\data.txt.deploy")));
            harness.AssertDeploymentHash(new FileInfo(Path.Combine(output, "App.application")),
                new FileInfo(Path.Combine(output, @"v1\App.manifest")));
        }

        [Fact]
        public async Task Cli_RealManifestSignaturesRefreshDeploymentIdentityAndSha256Hash()
        {
            AppInitializer.Initialize();
            using Harness harness = new(realManifestSigning: true);
            harness.Application(@"v1\App.manifest");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest");

            int exitCode = await harness.InvokeAsync("--clickonce-signing-version 2", deployment.Name);

            Assert.True(exitCode == ExitCode.Success,
                string.Join(Environment.NewLine, harness.SigningLogger.Entries.Select(entry => entry.Message)));
            Assert.Equal(2, harness.ManifestCalls.Count);
            Assert.Empty(harness.MageCalls);
            FileInfo application = new(Path.Combine(harness.Source.FullName, @"v1\App.manifest"));
            Assert.Contains("<Signature", System.IO.File.ReadAllText(application.FullName));
            Assert.Contains("<Signature", System.IO.File.ReadAllText(deployment.FullName));
            harness.AssertDeploymentHash(deployment, application);
        }

        [Theory]
        [InlineData("", false, false)]
        [InlineData("", true, false)]
        [InlineData("--no-sign-clickonce-deps", false, false)]
        [InlineData("--no-sign-clickonce-deps", true, false)]
        [InlineData("--no-update-clickonce-manifest", false, false)]
        [InlineData("--no-update-clickonce-manifest", true, false)]
        [InlineData("", false, true)]
        [InlineData("", true, true)]
        public async Task Cli_RecursiveVersion2PublishesSignedManifestsInRebuiltZip(
            string mode, bool outOfPlace, bool realManifestSigning)
        {
            if (realManifestSigning)
            {
                AppInitializer.Initialize();
            }
            using Harness harness = new(realManifestSigning);
            harness.File(@"publish\v1\payload.dll.deploy", "payload");
            harness.File(@"publish\unchanged.txt", "unchanged");
            harness.Application(@"publish\v1\App.manifest", "payload.dll");
            harness.Deployment(@"publish\App.application", @"v1\App.manifest", mapped: true);
            FileInfo bundle = new(Path.Combine(harness.Source.FullName, "bundle.zip"));
            ZipFile.CreateFromDirectory(Path.Combine(harness.Source.FullName, "publish"), bundle.FullName);
            byte[] originalBundle = System.IO.File.ReadAllBytes(bundle.FullName);
            string output = Path.Combine(harness.Source.FullName, "output");
            string outputOption = outOfPlace ? $"--output \"{output}\"" : "";

            int exitCode = await harness.InvokeAsync(
                $"--clickonce-signing-version 2 {mode} --max-concurrency 1 --recurse-containers {outputOption}", bundle.Name)
                .WaitAsync(TimeSpan.FromSeconds(30));

            Assert.True(exitCode == ExitCode.Success,
                string.Join(Environment.NewLine, harness.SigningLogger.Entries.Select(entry => entry.Message)));
            Assert.Equal(2, harness.ManifestCalls.Count);
            Assert.Empty(harness.MageCalls);
            Assert.Equal(mode.Length == 0 ? 1 : 0, harness.PayloadCalls.Count);
            using ZipArchive archive = ZipFile.OpenRead(
                outOfPlace ? Path.Combine(output, bundle.Name) : bundle.FullName);
            Dictionary<string, string> contents = archive.Entries.ToDictionary(
                entry => entry.FullName,
                entry =>
                {
                    using StreamReader reader = new(entry.Open());
                    return reader.ReadToEnd();
                });
            Assert.Equal(4, contents.Count);
            if (realManifestSigning)
            {
                Assert.Contains("<Signature", contents["App.application"]);
                Assert.Contains("<Signature", contents["v1/App.manifest"]);
            }
            else
            {
                Assert.EndsWith("\n<!--signed-->", contents["App.application"]);
                Assert.EndsWith("\n<!--signed-->", contents["v1/App.manifest"]);
            }
            Assert.Equal("unchanged", contents["unchanged.txt"]);
            Assert.Equal(mode.Length == 0 ? "payload signed" : "payload", contents["v1/payload.dll.deploy"]);
            if (mode != "--no-update-clickonce-manifest")
            {
                string verificationDirectory = Path.Combine(harness.Source.FullName, "verified");
                archive.ExtractToDirectory(verificationDirectory);
                harness.AssertDeploymentHash(
                    new FileInfo(Path.Combine(verificationDirectory, "App.application")),
                    new FileInfo(Path.Combine(verificationDirectory, @"v1\App.manifest")));
            }
            if (outOfPlace)
            {
                Assert.Equal(originalBundle, System.IO.File.ReadAllBytes(bundle.FullName));
            }
        }

        [Fact]
        public async Task Cli_ApplicationManifestPayloadIsMaterializedBeforeParentHashesIt()
        {
            using Harness harness = new();
            FileInfo inner = harness.Application("Inner.manifest");
            FileInfo outer = harness.Application("Outer.manifest", inner.Name);

            int exitCode = await harness.InvokeAsync("--clickonce-signing-version 2", outer.Name);

            Assert.True(exitCode == ExitCode.Success,
                string.Join(Environment.NewLine, harness.SigningLogger.Entries.Select(entry => entry.Message)));
            Assert.Equal(2, harness.ManifestCalls.Count);
            Assert.Empty(harness.PayloadCalls);
            Assert.Empty(harness.MageCalls);
            Assert.EndsWith("\n<!--signed-->", System.IO.File.ReadAllText(inner.FullName));
            Assert.EndsWith("\n<!--signed-->", System.IO.File.ReadAllText(outer.FullName));
            using FileStream stream = outer.OpenRead();
            Assert.True(new ClickOnceManifestReader().TryReadApplicationManifest(
                stream, out IApplicationManifest? manifest));
            using (manifest)
            {
                Assert.NotNull(manifest);
                FileReference reference = Assert.Single(manifest.FileReferences.Cast<FileReference>());
                byte[] signedInner = System.IO.File.ReadAllBytes(inner.FullName);
                Assert.Equal(signedInner.Length, reference.Size);
                Assert.Equal(Convert.ToBase64String(SHA256.HashData(signedInner)), reference.Hash);
            }
        }

        [Fact]
        public async Task Cli_FailedExplicitApplicationPreventsDeploymentSigning()
        {
            using Harness harness = new();
            harness.Application("App.manifest", "missing.dll");
            FileInfo deployment = harness.Deployment("App.application", "App.manifest");

            int exitCode = await harness.InvokeAsync(
                "--clickonce-signing-version 2 --no-sign-clickonce-deps", deployment.Name, "App.manifest");

            Assert.Equal(ExitCode.Failed, exitCode);
            Assert.Empty(harness.ManifestCalls);
            Assert.Empty(harness.PayloadCalls);
        }

        [Theory]
        [InlineData("")]
        [InlineData("--clickonce-signing-version 1")]
        [InlineData("--clickonce-signing-version 2")]
        public async Task Cli_OtherFormatsKeepTheirOrdinaryPath(string options)
        {
            using Harness harness = new();
            harness.File("ordinary.dll", "ordinary");
            FileInfo fusion = harness.File("Fusion.manifest",
                "<assembly xmlns='urn:schemas-microsoft-com:asm.v1' manifestVersion='1.0'><assemblyIdentity name='Fusion' version='1.0.0.0' type='win32'/></assembly>");
            string fusionContents = System.IO.File.ReadAllText(fusion.FullName);
            harness.File("Malformed.manifest", "<assembly>");

            int exitCode = await harness.InvokeAsync(options, "ordinary.dll", fusion.Name, "Malformed.manifest");

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(".dll", Assert.Single(harness.PayloadCalls).Extension);
            Assert.Equal("ordinary signed", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, "ordinary.dll")));
            Assert.Empty(harness.ManifestCalls);
            Assert.Empty(harness.MageCalls);
            Assert.Equal(fusionContents, System.IO.File.ReadAllText(fusion.FullName));
        }

        [Theory]
        [InlineData("missing", "")]
        [InlineData("malformed", "")]
        [InlineData("deployment", "")]
        [InlineData("mutation", "")]
        [InlineData("missing", "--no-sign-clickonce-deps")]
        [InlineData("malformed", "--no-sign-clickonce-deps")]
        [InlineData("deployment", "--no-sign-clickonce-deps")]
        [InlineData("mutation", "--no-sign-clickonce-deps")]
        [InlineData("missing", "--no-update-clickonce-manifest")]
        [InlineData("malformed", "--no-update-clickonce-manifest")]
        [InlineData("deployment", "--no-update-clickonce-manifest")]
        [InlineData("mutation", "--no-update-clickonce-manifest")]
        public async Task Cli_ClassificationCannotFallBackAfterOrdinarySigningChangesSource(string change, string mode)
        {
            using Harness harness = new();
            FileInfo application = harness.Application("App.manifest");
            harness.File("trigger.dll", "trigger");
            harness.BeforePayloadSigning = () =>
            {
                switch (change)
                {
                    case "missing":
                        application.Delete();
                        break;
                    case "malformed":
                        System.IO.File.WriteAllText(application.FullName, "<assembly>");
                        break;
                    case "deployment":
                        harness.Deployment(application.Name, "Other.manifest");
                        break;
                    case "mutation":
                        System.IO.File.AppendAllText(application.FullName, "\n<!--changed-->");
                        break;
                }
            };

            int exitCode = await harness.InvokeAsync($"--clickonce-signing-version 2 {mode}",
                application.Name, "trigger.dll");

            Assert.Equal(ExitCode.Failed, exitCode);
            Assert.Empty(harness.ManifestCalls);
            Assert.Empty(harness.MageCalls);
            Assert.Contains(harness.Logger.Entries,
                entry => entry.Message.Contains("changed or became unreadable after classification", StringComparison.Ordinal));
        }

        private sealed class Harness : IDisposable
        {
            private readonly DirectoryService _directories = new(NullLogger<IDirectoryService>.Instance);
            private readonly TemporaryDirectory _directory;
            private readonly ConcurrentQueue<RSA> _keys = new();
            private readonly ServiceProviderFactory _factory = new();
            internal DirectoryInfo Source => _directory.Directory;
            internal ConcurrentQueue<FileInfo> ManifestCalls { get; } = new();
            internal ConcurrentQueue<FileInfo> PayloadCalls { get; } = new();
            internal ConcurrentQueue<string> MageCalls { get; } = new();
            internal TestLogger<IDataFormatSigner> Logger { get; } = new();
            internal TestLogger<ISigner> SigningLogger { get; } = new();
            internal Action? BeforePayloadSigning { get; set; }

            internal Harness(bool realManifestSigning = false)
            {
                _directory = new TemporaryDirectory(_directories);
                ICertificateProvider certificateProvider = Substitute.For<ICertificateProvider>();
                certificateProvider.GetCertificateAsync().Returns(_ =>
                {
                    X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate();
                    _keys.Enqueue(certificate.GetRSAPrivateKey()!);
                    return Task.FromResult(certificate);
                });
                ISignatureAlgorithmProvider signatureProvider = Substitute.For<ISignatureAlgorithmProvider>();
                signatureProvider.GetRsaAsync().Returns(_ =>
                {
                    Assert.True(_keys.TryDequeue(out RSA? key));
                    return Task.FromResult(key!);
                });
                IManifestSigner manifestSigner = Substitute.For<IManifestSigner>();
                manifestSigner.When(signer => signer.Sign(
                    Arg.Any<FileInfo>(), Arg.Any<X509Certificate2>(), Arg.Any<RSA>(), Arg.Any<SignOptions>()))
                    .Do(call =>
                    {
                        FileInfo file = (FileInfo)call[0];
                        ManifestCalls.Enqueue(file);
                        if (realManifestSigning)
                        {
                            SignOptions options = (SignOptions)call[3];
                            SignOptions withoutTimestamp = new(
                                options.ApplicationName, options.PublisherName,
                                options.Description, options.DescriptionUrl,
                                options.FileHashAlgorithm, options.TimestampHashAlgorithm,
                                null!, options.Matcher, options.AntiMatcher, options.RecurseContainers);
                            new ManifestSigner().Sign(file, (X509Certificate2)call[1], (RSA)call[2], withoutTimestamp);
                        }
                        else
                        {
                            System.IO.File.AppendAllText(file.FullName, "\n<!--signed-->");
                        }
                    });
                IMageCli mage = Substitute.For<IMageCli>();
                mage.RunAsync(Arg.Any<string>()).Returns(call =>
                {
                    MageCalls.Enqueue((string)call[0]);
                    return Task.FromResult(0);
                });
                IDataFormatSigner payloadSigner = Substitute.For<IDataFormatSigner>();
                payloadSigner.CanSign(Arg.Any<FileInfo>()).Returns(call => ((FileInfo)call[0]).Extension is ".dll" or ".exe");
                payloadSigner.SignAsync(Arg.Any<IEnumerable<FileInfo>>(), Arg.Any<SignOptions>()).Returns(call =>
                {
                    foreach (FileInfo file in (IEnumerable<FileInfo>)call[0])
                    {
                        BeforePayloadSigning?.Invoke();
                        PayloadCalls.Enqueue(file);
                        System.IO.File.AppendAllText(file.FullName, " signed");
                    }
                    return Task.CompletedTask;
                });
                payloadSigner.SignAsync(
                    Arg.Any<SigningFile>(), Arg.Any<SignOptions>(), Arg.Any<SigningOperationCoordinator>())
                    .Returns(call =>
                    {
                        FileInfo file = ((SigningFile)call[0]).File;
                        BeforePayloadSigning?.Invoke();
                        PayloadCalls.Enqueue(file);
                        System.IO.File.AppendAllText(file.FullName, " signed");
                        return Task.CompletedTask;
                    });
                _factory.AddServices(services =>
                {
                    services.Remove(services.Single(descriptor => descriptor.ImplementationType == typeof(AzureSignToolSigner)));
                    services.AddSingleton(payloadSigner);
                    services.AddSingleton(certificateProvider);
                    services.AddSingleton(signatureProvider);
                    services.AddSingleton(Substitute.For<ICertificateVerifier>());
                    services.AddSingleton(manifestSigner);
                    services.AddSingleton(mage);
                    services.AddSingleton<ILogger<IDataFormatSigner>>(Logger);
                    services.AddSingleton<ILogger<ISigner>>(SigningLogger);
                });
            }

            internal async Task<int> InvokeAsync(string options, params string[] files)
            {
                SignCommand command = Program.CreateCommand(_factory);
                ParseResult result = command.Parse(
                    $"code {options} -b \"{Source.FullName}\" azure-key-vault -kvu https://keyvault.test -kvc certificate " +
                    string.Join(" ", files.Select(file => $"\"{file}\"")));
                Assert.Empty(result.Errors);
                return await result.InvokeAsync();
            }

            internal FileInfo File(string path, string content)
            {
                FileInfo file = new(Path.Combine(Source.FullName, path));
                file.Directory!.Create();
                System.IO.File.WriteAllText(file.FullName, content);
                return file;
            }

            internal FileInfo Application(string path, params string[] payloads)
            {
                ApplicationManifest manifest = new();
                SetIdentity(manifest);
                foreach (string payload in payloads)
                {
                    manifest.FileReferences.Add(new FileReference { TargetPath = payload });
                }
                return Write(path, manifest);
            }

            internal FileInfo Deployment(string path, string application, bool mapped = false)
            {
                DeployManifest manifest = new() { MapFileExtensions = mapped };
                SetIdentity(manifest);
                AssemblyReference reference = new(application);
                manifest.AssemblyReferences.Add(reference);
                manifest.EntryPoint = reference;
                return Write(path, manifest);
            }

            private static void SetIdentity(Manifest manifest)
            {
                manifest.AssemblyIdentity.Name = "Test";
                manifest.AssemblyIdentity.Version = "1.0.0.0";
                manifest.AssemblyIdentity.ProcessorArchitecture = "msil";
            }

            private FileInfo Write(string path, Manifest manifest)
            {
                FileInfo file = new(Path.Combine(Source.FullName, path));
                file.Directory!.Create();
                using FileStream stream = file.Create();
                ManifestWriter.WriteManifest(manifest, stream);
                return file;
            }

            internal void AssertDeploymentHash(FileInfo deployment, FileInfo application)
            {
                using FileStream stream = deployment.OpenRead();
                Assert.True(new ClickOnceManifestReader().TryReadDeployManifest(stream, out IDeployManifest? manifest));
                using (manifest)
                {
                    Assert.NotNull(manifest);
                    byte[] bytes = System.IO.File.ReadAllBytes(application.FullName);
                    Assert.Equal(bytes.Length, manifest.EntryPoint!.Size);
                    Assert.Equal(Convert.ToBase64String(SHA256.HashData(bytes)), manifest.EntryPoint.Hash);
                    Assert.Equal("Test", manifest.EntryPoint.AssemblyIdentity.Name);
                    using FileStream applicationStream = application.OpenRead();
                    Assert.True(new ClickOnceManifestReader().TryReadApplicationManifest(
                        applicationStream, out IApplicationManifest? applicationManifest));
                    using (applicationManifest)
                    {
                        Assert.NotNull(applicationManifest);
                        Assert.Equal(applicationManifest.AssemblyIdentity.PublicKeyToken,
                            manifest.EntryPoint.AssemblyIdentity.PublicKeyToken);
                    }
                }
            }

            public void Dispose()
            {
                while (_keys.TryDequeue(out RSA? key))
                {
                    key.Dispose();
                }
                _directory.Dispose();
                _directories.Dispose();
            }
        }
    }
}
