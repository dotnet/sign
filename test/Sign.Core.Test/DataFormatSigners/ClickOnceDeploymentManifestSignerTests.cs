// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed partial class ClickOnceDeploymentManifestSignerTests
    {
        [Fact]
        public void DefaultRegistration_KeepsBothVersion2PipelinesDormant()
        {
            using ServiceProvider services = ServiceProvider.CreateDefault(addServices: registrations =>
            {
                registrations.AddSingleton(Substitute.For<ICertificateProvider>());
                registrations.AddSingleton(Substitute.For<ISignatureAlgorithmProvider>());
            });
            IDataFormatSigner[] signers = services.GetServices<IDataFormatSigner>().ToArray();

            Assert.Single(signers.OfType<ClickOnceSigner>());
            Assert.False(typeof(IDataFormatSigner).IsAssignableFrom(typeof(ClickOnceDeploymentManifestSigner)));
            Assert.False(typeof(IDataFormatSigner).IsAssignableFrom(typeof(ClickOnceApplicationManifestSigner)));
            Assert.Null(services.GetService<ClickOnceDeploymentManifestSigner>());
            Assert.Null(services.GetService<ClickOnceApplicationManifestSigner>());
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task Default_UpdatesSha256AndPublishesOnlySelectedVersion(bool mapped, bool outOfPlace)
        {
            await using Harness harness = new();
            string payloadPath = mapped ? @"v1\payload.dll.deploy" : @"v1\payload.dll";
            string dataPath = mapped ? @"v1\data.txt.deploy" : @"v1\data.txt";
            string excludedPath = mapped ? @"v1\excluded.dll.deploy" : @"v1\excluded.dll";
            harness.File(payloadPath, "payload");
            harness.File(dataPath, "data");
            harness.File(excludedPath, "excluded");
            harness.Application(@"v1\App.manifest", "payload.dll", "data.txt", "excluded.dll");
            Matcher antiMatcher = new();
            antiMatcher.AddInclude("**/excluded.dll*");
            harness.Options = new(null, null, null, null, HashAlgorithmName.SHA256, HashAlgorithmName.SHA256,
                new Uri("https://timestamp.test"), null, antiMatcher, true);
            harness.Application(@"v2\Other.manifest", "old.dll");
            harness.File(@"v2\old.dll", "old");
            harness.File("setup.exe", "setup");
            harness.File("unrelated.exe", "unrelated");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest", mapped);
            FileInfo output = outOfPlace ? harness.Output(deployment.Name) : deployment;

            await harness.ExecuteAsync(deployment, output);

            Assert.Equal(new[] { "payload.dll", "App.manifest", "App.application", "setup.exe" },
                harness.Calls.ToArray());
            Assert.Equal("payload signed", System.IO.File.ReadAllText(Path.Combine(output.DirectoryName!, payloadPath)));
            Assert.Equal("old", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, @"v2\old.dll")));
            Assert.Equal("unrelated", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, "unrelated.exe")));
            Assert.Equal("data", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, dataPath)));
            Assert.Equal("excluded", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, excludedPath)));
            harness.AssertHash(output, new FileInfo(Path.Combine(output.DirectoryName!, @"v1\App.manifest")));
            if (outOfPlace)
            {
                Assert.Equal(4, Directory.GetFiles(output.DirectoryName!, "*", SearchOption.AllDirectories).Length);
                Assert.False(System.IO.File.Exists(Path.Combine(output.DirectoryName!, @"v1\data.txt")));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Default_DeploymentDirectoryFallbackKeepsPublishedLocation(bool outOfPlace)
        {
            await using Harness harness = new();
            harness.File("payload.dll.deploy", "fallback");
            harness.Application(@"v1\App.manifest", "payload.dll");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest", true);
            FileInfo output = outOfPlace ? harness.Output(deployment.Name) : deployment;

            await harness.ExecuteAsync(deployment, output);

            Assert.Equal("fallback signed", System.IO.File.ReadAllText(Path.Combine(output.DirectoryName!, "payload.dll.deploy")));
            Assert.False(System.IO.File.Exists(Path.Combine(output.DirectoryName!, @"v1\payload.dll.deploy")));
            harness.AssertHash(output, new FileInfo(Path.Combine(output.DirectoryName!, @"v1\App.manifest")));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NoSignDependencies_UpdatesAndPublishesOnlyExplicitDeployment(bool outOfPlace)
        {
            await using Harness harness = new();
            FileInfo application = harness.Application("App.manifest", "missing.dll");
            harness.File("setup.exe", "setup");
            FileInfo deployment = harness.Deployment("App.application", application.Name);
            string originalApplication = System.IO.File.ReadAllText(application.FullName);
            FileInfo output = outOfPlace ? harness.Output(deployment.Name) : deployment;

            await harness.ExecuteAsync(deployment, output, ClickOnceApplicationSigningPolicy.UpdateManifestOnly);

            Assert.Equal(new[] { "App.application" }, harness.Calls.ToArray());
            Assert.Equal(originalApplication, System.IO.File.ReadAllText(application.FullName));
            Assert.Equal("setup", System.IO.File.ReadAllText(Path.Combine(harness.Source.FullName, "setup.exe")));
            harness.AssertHash(output, application);
            if (outOfPlace)
            {
                Assert.Single(Directory.GetFiles(output.DirectoryName!, "*", SearchOption.AllDirectories));
            }
        }

        [Theory]
        [InlineData(".application")]
        [InlineData(".vsto")]
        public async Task NoUpdate_RequiresNoGraphAndPreservesOriginalBytes(string extension)
        {
            await using Harness harness = new();
            FileInfo deployment = harness.Deployment($"App{extension}", "missing.manifest");
            string original = System.IO.File.ReadAllText(deployment.FullName);
            FileInfo output = harness.Output(deployment.Name);

            await harness.ExecuteAsync(deployment, output, updateManifest: false);

            Assert.Equal(new[] { deployment.Name }, harness.Calls.ToArray());
            Assert.Equal(original + "\n<!--signed-->", System.IO.File.ReadAllText(output.FullName));
            Assert.Equal(original, System.IO.File.ReadAllText(deployment.FullName));
            Assert.Single(Directory.GetFiles(output.DirectoryName!, "*", SearchOption.AllDirectories));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task NoUpdate_DoesNotApplyDeploymentStructuralGraphValidation(int references)
        {
            await using Harness harness = new();
            DeployManifest manifest = Harness.NewDeployment();
            for (int i = 0; i < references; ++i)
            {
                AssemblyReference reference = new($"missing{i}.manifest") { IsOptional = true };
                manifest.AssemblyReferences.Add(reference);
                manifest.EntryPoint = reference;
            }
            manifest.FileReferences.Add(new FileReference { TargetPath = "unrelated.data" });
            FileInfo deployment = harness.Write("App.application", manifest);
            string original = System.IO.File.ReadAllText(deployment.FullName);

            await harness.ExecuteAsync(deployment, updateManifest: false);

            Assert.Equal(original + "\n<!--signed-->", System.IO.File.ReadAllText(deployment.FullName));
            Assert.Single(harness.Calls);
        }

        [Fact]
        public async Task NoUpdate_BatchSignsExplicitApplicationIndependentlyWithoutResolvingGraph()
        {
            RecordingReader reader = new();
            await using Harness harness = new(reader: reader);
            FileInfo application = harness.Application("App.manifest", "missing.dll");
            FileInfo deployment = harness.Deployment("App.application", application.Name);
            string originalApplication = System.IO.File.ReadAllText(application.FullName);

            await harness.Pipeline.ExecuteAsync(new[]
            {
                new SigningOperationPlan(SigningFile.Capture(deployment), deployment),
                new SigningOperationPlan(SigningFile.Capture(application), application)
            }, harness.Options, harness.Coordinator, 1, updateManifest: false);

            Assert.Equal(2, reader.ApplicationReads);
            Assert.Empty(reader.DeploymentResolves);
            Assert.Equal(0, reader.DeploymentUpdates);
            Assert.Equal(0, reader.ApplicationUpdates);
            Assert.Empty(reader.ApplicationResolves);
            Assert.Equal(originalApplication + "\n<!--signed-->", System.IO.File.ReadAllText(application.FullName));
            Assert.Equal(new[] { application.Name, deployment.Name }, harness.Calls.ToArray());
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        public async Task EveryMode_RejectsMalformedAndWrongTypeBeforeSigning(int mode, bool wrongType)
        {
            await using Harness harness = new();
            FileInfo file = wrongType
                ? harness.Application("Wrong.application")
                : harness.File("Broken.vsto", "<assembly>");
            string original = System.IO.File.ReadAllText(file.FullName);

            ClickOncePublishLayoutResolutionException failure =
                await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(
                    () => harness.ExecuteAsync(file, policy: Policy(mode), updateManifest: mode != 2));

            Assert.Contains(file.FullName, failure.Message);
            Assert.Empty(harness.Calls);
            Assert.Equal(original, System.IO.File.ReadAllText(file.FullName));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task UpdatingModes_MissingApplicationDiagnosticIncludesPathAndNoUpdateSuggestion(int mode)
        {
            await using Harness harness = new();
            FileInfo deployment = harness.Deployment("App.application", @"missing\App.manifest");

            ClickOncePublishLayoutResolutionException failure =
                await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(
                    () => harness.ExecuteAsync(deployment, policy: Policy(mode)));

            Assert.Contains(Path.Combine(harness.Source.FullName, @"missing\App.manifest"), failure.Message);
            Assert.Contains("--clickonce-signing-version 2 --no-update-clickonce-manifest", failure.Message);
            Assert.Empty(harness.Calls);
        }

        [Theory]
        [InlineData(1, false, 0, true)]
        [InlineData(1, true, 0, true)]
        [InlineData(4, false, 0, true)]
        [InlineData(4, true, 0, true)]
        [InlineData(1, false, 1, true)]
        [InlineData(1, true, 1, true)]
        [InlineData(4, false, 1, true)]
        [InlineData(4, true, 1, true)]
        [InlineData(1, false, 0, false)]
        [InlineData(1, true, 0, false)]
        [InlineData(4, false, 0, false)]
        [InlineData(4, true, 0, false)]
        [InlineData(1, false, 1, false)]
        [InlineData(1, true, 1, false)]
        [InlineData(4, false, 1, false)]
        [InlineData(4, true, 1, false)]
        public async Task ExplicitPrerequisites_ReverseAndParallelPlansFinishBeforeDeploymentSlots(
            int maxConcurrency, bool deploymentFirst, int mode, bool outOfPlace)
        {
            await using Harness harness = new();
            harness.File("payload.dll", "payload");
            FileInfo application = harness.Application("App.manifest", "payload.dll");
            FileInfo[] deployments = Enumerable.Range(0, 7)
                .Select(index => harness.Deployment($"App{index}.application", application.Name)).ToArray();
            List<SigningOperationPlan> plans = deployments
                .Select(file => new SigningOperationPlan(SigningFile.Capture(file), Destination(file))).ToList();
            SigningOperationPlan applicationPlan = new(SigningFile.Capture(application), Destination(application));
            if (deploymentFirst)
            {
                plans.Add(applicationPlan);
            }
            else
            {
                plans.Insert(0, applicationPlan);
            }

            await harness.Pipeline.ExecuteAsync(
                plans, harness.Options, harness.Coordinator, maxConcurrency, Policy(mode))
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal("App.manifest", harness.Calls.First(call => call.EndsWith(".manifest") || call.EndsWith(".application")));
            Assert.Equal(8, harness.Calls.Count(call => call.EndsWith(".manifest") || call.EndsWith(".application")));
            Assert.Equal(1, harness.Calls.Count(call => call == application.Name));
            foreach (FileInfo deployment in deployments)
            {
                harness.AssertHash(Destination(deployment), Destination(application));
            }

            FileInfo Destination(FileInfo file) => outOfPlace ? harness.Output(file.Name) : file;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task ExplicitPrerequisiteFailure_PreventsDeploymentSigning(int mode)
        {
            await using Harness harness = new();
            FileInfo application = harness.Application("App.manifest", "missing.dll");
            FileInfo deployment = harness.Deployment("App.application", application.Name);

            await Assert.ThrowsAnyAsync<Exception>(() => harness.Pipeline.ExecuteAsync(
                new[]
                {
                    new SigningOperationPlan(SigningFile.Capture(deployment), deployment),
                    new SigningOperationPlan(SigningFile.Capture(application), application)
                },
                harness.Options, harness.Coordinator, 1, Policy(mode)));

            Assert.Empty(harness.Calls);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task EveryMode_PreservesSigningOptions(int mode)
        {
            await using Harness harness = new();
            harness.Application("App.manifest");
            FileInfo deployment = harness.Deployment("App.application", "App.manifest");
            SignOptions options = new(
                "Application", "Publisher", "Description", new Uri("https://support.test"),
                HashAlgorithmName.SHA512, HashAlgorithmName.SHA384, new Uri("https://timestamp.test"), null, null, true);
            harness.Options = options;

            await harness.ExecuteAsync(deployment, policy: Policy(mode), updateManifest: mode != 2);

            Assert.All(harness.ManifestOptions, actual => Assert.Same(options, actual));
            if (mode != 2)
            {
                harness.AssertHash(deployment, new FileInfo(Path.Combine(harness.Source.FullName, "App.manifest")));
                XElement description = XDocument.Load(deployment.FullName).Descendants()
                    .Single(element => element.Name.LocalName == "description");
                Assert.Contains(description.Attributes(), attribute => attribute.Name.LocalName == "product" && attribute.Value == "Application");
                Assert.Contains(description.Attributes(), attribute => attribute.Name.LocalName == "publisher" && attribute.Value == "Publisher");
                Assert.Contains(description.Attributes(), attribute => attribute.Name.LocalName == "supportUrl" && attribute.Value == "https://support.test/");
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task EveryMode_HasExactResolutionAndMetadataBoundaries(int mode)
        {
            RecordingReader reader = new();
            await using Harness harness = new(reader: reader);
            harness.Application("App.manifest");
            FileInfo deployment = harness.Deployment("App.application", "App.manifest");

            await harness.ExecuteAsync(deployment, policy: Policy(mode), updateManifest: mode != 2);

            Assert.Equal(1, reader.DeploymentReads);
            Assert.Equal(mode == 2 ? 0 : 2, reader.ApplicationReads);
            Assert.Equal(mode == 2 ? 0 : 1, reader.DeploymentResolves.Count);
            Assert.Equal(mode == 2 ? 0 : 1, reader.DeploymentUpdates);
            Assert.Equal(mode == 0 ? 1 : 0, reader.ApplicationResolves.Count);
            Assert.Equal(mode == 0 ? 1 : 0, reader.ApplicationUpdates);
            Assert.Equal(mode == 0 ? 1 : 0, reader.ApplicationIdentityUpdates);
            if (mode != 2)
            {
                Assert.Equal(harness.Source.FullName, Assert.Single(Assert.Single(reader.DeploymentResolves)).FullName);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task EveryMode_ReadFailureNeverSigns(int mode)
        {
            await using Harness harness = new();
            FileInfo missing = new(Path.Combine(harness.Source.FullName, "Missing.application"));

            ClickOncePublishLayoutResolutionException failure =
                await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(
                    () => harness.ExecuteAsync(missing, policy: Policy(mode), updateManifest: mode != 2));

            Assert.Contains(missing.FullName, failure.Message);
            Assert.Empty(harness.Calls);
        }

        [Fact]
        public async Task ExplicitUserMatching_UnrelatedFormatsRemainOnOrdinaryPathExactlyOnce()
        {
            await using Harness harness = new();
            harness.Application("App.manifest");
            FileInfo deployment = harness.Deployment("App.application", "App.manifest");
            FileInfo payload = harness.File("unrelated.dll", "ordinary");
            FileInfo fusion = harness.File("Fusion.manifest",
                "<assembly xmlns='urn:schemas-microsoft-com:asm.v1' manifestVersion='1.0'><assemblyIdentity name='Fusion' version='1.0.0.0' type='win32'/></assembly>");
            string originalFusion = System.IO.File.ReadAllText(fusion.FullName);

            await harness.Pipeline.ExecuteAsync(new[]
            {
                new SigningOperationPlan(SigningFile.Capture(deployment), deployment),
                new SigningOperationPlan(SigningFile.Capture(payload), payload),
                new SigningOperationPlan(SigningFile.Capture(fusion), fusion)
            }, harness.Options, harness.Coordinator, 1);

            Assert.Equal(1, harness.Calls.Count(call => call.EndsWith(".dll")));
            Assert.Equal("ordinary signed", System.IO.File.ReadAllText(payload.FullName));
            Assert.Equal(originalFusion, System.IO.File.ReadAllText(fusion.FullName));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task UpdatingModes_PreserveDiagnosticSeveritiesWhenRequiredResolutionSucceeds(int mode)
        {
            RecordingReader reader = new();
            reader.DeploymentDiagnostics.Add(new("deployment", "deployment error", OutputMessageType.Error));
            reader.ApplicationDiagnostics.Add(new("application", "application warning", OutputMessageType.Warning));
            await using Harness harness = new(reader: reader);
            harness.Application("App.manifest");
            FileInfo deployment = harness.Deployment("App.application", "App.manifest");

            await harness.ExecuteAsync(deployment, policy: Policy(mode));

            Assert.Contains(harness.Logger.Entries,
                entry => entry.LogLevel == LogLevel.Error && entry.Message == "deployment error");
            Assert.Contains(harness.Logger.Entries,
                entry => entry.LogLevel == LogLevel.Warning && entry.Message == "application warning");
            Assert.Contains(deployment.Name, harness.Calls);
        }

        [Fact]
        public async Task Default_OrdersPayloadIdentityApplicationAndDeploymentUpdatesBeforeAdjacentSigning()
        {
            RecordingReader reader = new();
            await using Harness harness = new(reader: reader);
            harness.File("payload.dll", "payload");
            harness.Application("App.manifest", "payload.dll");
            harness.File("setup.exe", "setup");
            FileInfo deployment = harness.Deployment("App.application", "App.manifest");
            harness.BeforeSign = file => reader.Operations.Add(file.Name);

            await harness.ExecuteAsync(deployment);

            Assert.Equal(new[]
            {
                "deployment resolve", "application resolve", "payload.dll", "application identity",
                "application update", "App.manifest", "deployment update", "App.application", "setup.exe"
            }, reader.Operations);
        }

        [Fact]
        public async Task UpdatingDeployment_NewErrorsPreventSigningAndPublication()
        {
            RecordingReader reader = new();
            reader.BeforeDeploymentUpdate = () => reader.DeploymentDiagnostics.Add(
                new("update", "new deployment update error", OutputMessageType.Error));
            await using Harness harness = new(reader: reader);
            harness.Application("App.manifest");
            FileInfo deployment = harness.Deployment("App.application", "App.manifest");
            string original = System.IO.File.ReadAllText(deployment.FullName);

            await Assert.ThrowsAsync<SigningException>(() => harness.ExecuteAsync(deployment,
                policy: ClickOnceApplicationSigningPolicy.UpdateManifestOnly));

            Assert.Empty(harness.Calls);
            Assert.Equal(original, System.IO.File.ReadAllText(deployment.FullName));
            Assert.Contains(harness.Logger.Entries,
                entry => entry.LogLevel == LogLevel.Error && entry.Message == "new deployment update error");
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(0, true)]
        public async Task EveryMode_PreservesRealVstoApplicationAndDeploymentSubtrees(int mode, bool realSigning)
        {
            if (realSigning)
            {
                AppInitializer.Initialize();
            }
            await using Harness harness = new(realSigning);
            FileInfo application = harness.File("App.manifest", System.IO.File.ReadAllText(
                ClickOnceResolutionTestUtilities.GetVstoManifestPath()));
            System.IO.File.Copy(typeof(ClickOnceDeploymentManifestSignerTests).Assembly.Location,
                Path.Combine(harness.Source.FullName, "payload.dll"));
            FileInfo deployment = harness.Deployment("AddIn.vsto", application.Name);
            XDocument document = XDocument.Load(deployment.FullName);
            XElement subtree = XElement.Parse(
                "<vsta:customization xmlns:vsta='urn:schemas-microsoft-com:vsta.v3' xmlns:vsto='urn:schemas-microsoft-com:vsto.v4'><vsto:appAddIn keyName='KeepThis'/></vsta:customization>");
            document.Root!.Add(subtree);
            document.Save(deployment.FullName);
            XElement applicationSubtree = XDocument.Load(application.FullName).Descendants()
                .Single(element => element.Name == XName.Get("addIn", "urn:schemas-microsoft-com:vsta.v3"));

            await harness.ExecuteAsync(deployment, policy: Policy(mode), updateManifest: mode != 2);

            Assert.True(XNode.DeepEquals(subtree, XDocument.Load(deployment.FullName).Descendants()
                .Single(element => element.Name == subtree.Name)));
            Assert.True(XNode.DeepEquals(applicationSubtree, XDocument.Load(application.FullName).Descendants()
                .Single(element => element.Name == applicationSubtree.Name)));
            Assert.False(System.IO.File.Exists(Path.Combine(harness.Source.FullName, "AddIn.vsto.bak")));
            if (realSigning)
            {
                Assert.Contains("<Signature", System.IO.File.ReadAllText(application.FullName));
                Assert.Contains("<Signature", System.IO.File.ReadAllText(deployment.FullName));
                harness.AssertHash(deployment, application);
            }
        }

        private static ClickOnceApplicationSigningPolicy Policy(int mode) =>
            mode == 1 ? ClickOnceApplicationSigningPolicy.UpdateManifestOnly : ClickOnceApplicationSigningPolicy.SignPayloads;

        [Fact]
        public async Task NoSignDependencies_UnsupportedExplicitPayloadIsNotPromisedAsSigningWork()
        {
            await using Harness harness = new();
            FileInfo data = harness.File("data.txt", "unsigned data");
            FileInfo application = harness.Application("App.manifest", data.Name);
            FileInfo deployment = harness.Deployment("App.application", application.Name);

            await harness.Pipeline.ExecuteAsync(new[]
            {
                new SigningOperationPlan(SigningFile.Capture(deployment), harness.Output(deployment.Name)),
                new SigningOperationPlan(SigningFile.Capture(application), harness.Output(application.Name)),
                new SigningOperationPlan(SigningFile.Capture(data), harness.Output(data.Name))
            }, harness.Options, harness.Coordinator, 1, ClickOnceApplicationSigningPolicy.UpdateManifestOnly);

            Assert.Equal(new[] { application.Name, deployment.Name }, harness.Calls.ToArray());
            Assert.Equal("unsigned data", System.IO.File.ReadAllText(harness.Output(data.Name).FullName));
            harness.AssertHash(harness.Output(deployment.Name), harness.Output(application.Name));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Default_FallbackNestedPrimaryAndCompanionsKeepOnePublicationOrigin(
            bool explicitChild, bool outOfPlace)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File(@"Other\payload.dll", "payload");
            FileInfo other = harness.Application(@"Other\Other.manifest", payload.Name);
            FileInfo child = harness.Deployment("Child.application", @"Other\Other.manifest");
            FileInfo application = harness.Application(@"v1\App.manifest", child.Name);
            FileInfo deployment = harness.Deployment("Root.application", @"v1\App.manifest");
            FileInfo[] files = new[] { payload, other, child, application, deployment };
            Dictionary<string, byte[]> originals = files.ToDictionary(
                file => file.FullName, file => System.IO.File.ReadAllBytes(file.FullName));
            FileInfo Destination(FileInfo file) => outOfPlace
                ? harness.Output(Path.GetRelativePath(harness.Source.FullName, file.FullName))
                : file;

            if (explicitChild)
            {
                await harness.Pipeline.ExecuteAsync(new[]
                {
                    new SigningOperationPlan(SigningFile.Capture(deployment), Destination(deployment)),
                    new SigningOperationPlan(SigningFile.Capture(child), Destination(child))
                }, harness.Options, harness.Coordinator, 1).WaitAsync(TimeSpan.FromSeconds(20));
            }
            else
            {
                await harness.ExecuteAsync(deployment, Destination(deployment)).WaitAsync(TimeSpan.FromSeconds(20));
            }

            harness.AssertHash(Destination(deployment), Destination(application));
            harness.AssertHash(Destination(child), Destination(other));
            harness.AssertPayloadHash(Destination(application), Destination(child));
            harness.AssertPayloadHash(Destination(other), Destination(payload));
            Assert.Equal(1, harness.Calls.Count(name => name == child.Name));
            Assert.Equal("payload signed", System.IO.File.ReadAllText(Destination(payload).FullName));
            Assert.False(System.IO.File.Exists(Path.Combine(Destination(deployment).DirectoryName!,
                @"v1\Other\Other.manifest")));
            Assert.False(System.IO.File.Exists(Path.Combine(Destination(deployment).DirectoryName!,
                @"v1\Other\payload.dll")));
            if (outOfPlace)
            {
                Assert.Equal(new[] { @"Child.application", @"Other\Other.manifest", @"Other\payload.dll",
                    @"Root.application", @"v1\App.manifest" },
                    Directory.GetFiles(Destination(deployment).DirectoryName!, "*", SearchOption.AllDirectories)
                        .Select(path => Path.GetRelativePath(Destination(deployment).DirectoryName!, path))
                        .OrderBy(path => path, StringComparer.Ordinal).ToArray());
                Assert.All(originals, pair => Assert.Equal(pair.Value, System.IO.File.ReadAllBytes(pair.Key)));
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public async Task NoSignDependencies_ExplicitNestedDeploymentPrerequisiteStartsBeforeApplicationWait(int maxConcurrency)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File(@"Other\payload.dll", "payload");
            FileInfo other = harness.Application(@"Other\Other.manifest", payload.Name);
            FileInfo child = harness.Deployment("Child.application", @"Other\Other.manifest");
            FileInfo application = harness.Application("App.manifest", child.Name);
            FileInfo deployment = harness.Deployment("Root.application", application.Name);
            FileInfo[] files = new[] { deployment, application, child, other, payload };
            SigningOperationPlan[] plans = files.Select(file =>
                new SigningOperationPlan(SigningFile.Capture(file),
                    harness.Output(Path.GetRelativePath(harness.Source.FullName, file.FullName)))).ToArray();

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency,
                ClickOnceApplicationSigningPolicy.UpdateManifestOnly).WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(5, harness.Calls.Count);
            Assert.Equal(1, harness.Calls.Count(name => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)));
            Assert.True(Array.IndexOf(harness.Calls.ToArray(), other.Name) <
                Array.IndexOf(harness.Calls.ToArray(), child.Name));
            Assert.True(Array.IndexOf(harness.Calls.ToArray(), child.Name) <
                Array.IndexOf(harness.Calls.ToArray(), application.Name));
            harness.AssertHash(harness.Output(deployment.Name), harness.Output(application.Name));
            harness.AssertHash(harness.Output(child.Name), harness.Output(@"Other\Other.manifest"));
            Assert.Equal("payload", System.IO.File.ReadAllText(payload.FullName));
            Assert.Equal("payload signed", System.IO.File.ReadAllText(harness.Output(@"Other\payload.dll").FullName));
        }

        private sealed class RecordingReader : IClickOnceManifestReader
        {
            private readonly ClickOnceManifestReader _reader = new();
            internal int DeploymentReads { get; private set; }
            internal int ApplicationReads { get; private set; }
            internal int DeploymentUpdates { get; private set; }
            internal int ApplicationUpdates { get; private set; }
            internal int ApplicationIdentityUpdates { get; private set; }
            internal int DeploymentDisposals { get; private set; }
            internal int ApplicationDisposals { get; private set; }
            internal ConcurrentDictionary<string, int> DeploymentReadsByPath { get; } = new();
            internal ConcurrentDictionary<string, int> ApplicationReadsByPath { get; } = new();
            internal ConcurrentQueue<IClickOnceManifest> ParsedManifests { get; } = new();
            internal List<IReadOnlyList<DirectoryInfo>> DeploymentResolves { get; } = new();
            internal List<IReadOnlyList<DirectoryInfo>> ApplicationResolves { get; } = new();
            internal List<ClickOnceManifestDiagnostic> DeploymentDiagnostics { get; } = new();
            internal List<ClickOnceManifestDiagnostic> ApplicationDiagnostics { get; } = new();
            internal List<string> Operations { get; } = new();
            internal Action? BeforeDeploymentUpdate { get; set; }

            public bool TryReadDeployManifest(Stream stream, [NotNullWhen(true)] out IDeployManifest? manifest)
            {
                ++DeploymentReads;
                DeploymentReadsByPath.AddOrUpdate(((FileStream)stream).Name, 1, (_, count) => count + 1);
                if (!_reader.TryReadDeployManifest(stream, out IDeployManifest? actual))
                {
                    manifest = null;
                    return false;
                }
                IDeployManifest spy = Substitute.For<IDeployManifest>();
                spy.MapFileExtensions.Returns(actual.MapFileExtensions);
                spy.HasUnsupportedResourceFallback.Returns(actual.HasUnsupportedResourceFallback);
                spy.When(value => value.ApplySigningOptions(Arg.Any<SignOptions>(), Arg.Any<string>()))
                    .Do(call => actual.ApplySigningOptions(call.Arg<SignOptions>(), call.Arg<string>()));
                Configure(actual, spy, true);
                manifest = spy;
                return true;
            }

            public bool TryReadApplicationManifest(Stream stream, [NotNullWhen(true)] out IApplicationManifest? manifest)
            {
                ++ApplicationReads;
                ApplicationReadsByPath.AddOrUpdate(((FileStream)stream).Name, 1, (_, count) => count + 1);
                if (!_reader.TryReadApplicationManifest(stream, out IApplicationManifest? actual))
                {
                    manifest = null;
                    return false;
                }
                IApplicationManifest spy = Substitute.For<IApplicationManifest>();
                Configure(actual, spy, false);
                spy.When(value => value.SynchronizeAssemblyIdentities()).Do(_ =>
                {
                    ++ApplicationIdentityUpdates;
                    Operations.Add("application identity");
                    actual.SynchronizeAssemblyIdentities();
                });
                manifest = spy;
                return true;
            }

            private void Configure(IClickOnceManifest actual, IClickOnceManifest spy, bool deployment)
            {
                ParsedManifests.Enqueue(spy);
                spy.AssemblyIdentity.Returns(_ => actual.AssemblyIdentity);
                spy.AssemblyReferences.Returns(actual.AssemblyReferences);
                spy.FileReferences.Returns(actual.FileReferences);
                spy.EntryPoint.Returns(_ => actual.EntryPoint);
                spy.Diagnostics.Returns(_ => actual.Diagnostics.Concat(
                    deployment ? DeploymentDiagnostics : ApplicationDiagnostics).ToArray());
                spy.ReadOnly.Returns(_ => actual.ReadOnly);
                spy.When(value => value.ReadOnly = false).Do(_ => actual.ReadOnly = false);
                spy.When(value => value.ReadOnly = true).Do(_ => actual.ReadOnly = true);
                spy.When(value => value.ResolveFiles(Arg.Any<IReadOnlyList<DirectoryInfo>>())).Do(call =>
                {
                    IReadOnlyList<DirectoryInfo> directories = call.Arg<IReadOnlyList<DirectoryInfo>>();
                    (deployment ? DeploymentResolves : ApplicationResolves).Add(directories);
                    Operations.Add(deployment ? "deployment resolve" : "application resolve");
                    actual.ResolveFiles(directories);
                });
                spy.When(value => value.UpdateFileInfo()).Do(_ =>
                {
                    if (deployment)
                    {
                        ++DeploymentUpdates;
                        BeforeDeploymentUpdate?.Invoke();
                    }
                    else
                    {
                        ++ApplicationUpdates;
                    }
                    Operations.Add(deployment ? "deployment update" : "application update");
                    actual.UpdateFileInfo();
                });
                spy.When(value => value.Write(Arg.Any<FileInfo>())).Do(call => actual.Write(call.Arg<FileInfo>()));
                spy.When(value => value.Dispose()).Do(_ =>
                {
                    if (deployment)
                    {
                        ++DeploymentDisposals;
                    }
                    else
                    {
                        ++ApplicationDisposals;
                    }
                    actual.Dispose();
                });
            }
        }

        private sealed partial class Harness : IAsyncDisposable
        {
            private readonly ServiceProvider _services;
            private readonly TemporaryDirectory _directory;
            private readonly ConcurrentQueue<RSA> _keys = new();
            private readonly IMageCli _mage = Substitute.For<IMageCli>();
            internal DirectoryInfo Source => _directory.Directory;
            internal SigningOperationCoordinator Coordinator { get; }
            internal ClickOnceDeploymentManifestSigner Pipeline { get; }
            internal ConcurrentQueue<string> Calls { get; } = new();
            internal ConcurrentQueue<SignOptions> ManifestOptions { get; } = new();
            internal TestLogger<IDataFormatSigner> Logger { get; } = new();
            internal Action<FileInfo>? BeforeSign { get; set; }
            internal Func<string, IDisposable>? WorkScope { get; set; }
            internal SignOptions Options { get; set; } = new(HashAlgorithmName.SHA256, new Uri("https://timestamp.test"));

            internal Harness(bool realSigning = false, IClickOnceManifestReader? reader = null)
            {
                _mage.RunAsync(Arg.Any<string?>()).Returns(_ => Task.FromException<int>(
                    new InvalidOperationException("Dormant version 2 must not invoke legacy Mage.")));
                ICertificateProvider certificates = Substitute.For<ICertificateProvider>();
                certificates.GetCertificateAsync().Returns(_ =>
                {
                    X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate();
                    _keys.Enqueue(certificate.GetRSAPrivateKey()!);
                    return Task.FromResult(certificate);
                });
                ISignatureAlgorithmProvider signatures = Substitute.For<ISignatureAlgorithmProvider>();
                signatures.GetRsaAsync().Returns(_ =>
                {
                    Assert.True(_keys.TryDequeue(out RSA? key));
                    return Task.FromResult(key!);
                });
                IManifestSigner manifestSigner = Substitute.For<IManifestSigner>();
                manifestSigner.When(signer => signer.Sign(
                    Arg.Any<FileInfo>(), Arg.Any<X509Certificate2>(), Arg.Any<RSA>(), Arg.Any<SignOptions>()))
                    .Do(call =>
                    {
                        using IDisposable? work = WorkScope?.Invoke("manifest signing");
                        FileInfo file = call.Arg<FileInfo>();
                        BeforeSign?.Invoke(file);
                        Calls.Enqueue(file.Name);
                        ManifestOptions.Enqueue(call.Arg<SignOptions>());
                        if (realSigning)
                        {
                            SignOptions options = call.Arg<SignOptions>();
                            SignOptions withoutTimestamp = new(
                                options.ApplicationName, options.PublisherName, options.Description, options.DescriptionUrl,
                                options.FileHashAlgorithm, options.TimestampHashAlgorithm, null!,
                                options.Matcher, options.AntiMatcher, options.RecurseContainers);
                            new ManifestSigner().Sign(file, call.Arg<X509Certificate2>(), call.Arg<RSA>(), withoutTimestamp);
                        }
                        else
                        {
                            System.IO.File.AppendAllText(file.FullName, "\n<!--signed-->");
                        }
                    });
                IDataFormatSigner payloadSigner = Substitute.For<IDataFormatSigner>();
                payloadSigner.CanSign(Arg.Any<FileInfo>()).Returns(call =>
                    call.Arg<FileInfo>().Extension is ".dll" or ".exe");
                payloadSigner.SignAsync(Arg.Any<IEnumerable<FileInfo>>(), Arg.Any<SignOptions>())
                    .Returns(call => SignPayloads(call.Arg<IEnumerable<FileInfo>>()));
                payloadSigner.SignAsync(
                    Arg.Any<SigningFile>(), Arg.Any<SignOptions>(), Arg.Any<SigningOperationCoordinator>())
                    .Returns(call => SignPayloads(new[] { call.Arg<SigningFile>().File }));

                Task SignPayloads(IEnumerable<FileInfo> files)
                {
                    foreach (FileInfo file in files)
                    {
                        using IDisposable? work = WorkScope?.Invoke("ordinary signing");
                        BeforeSign?.Invoke(file);
                        Calls.Enqueue(file.Name);
                        System.IO.File.AppendAllText(file.FullName, " signed");
                    }
                    return Task.CompletedTask;
                }
                _services = ServiceProvider.CreateDefault(LogLevel.None, addServices: services =>
                {
                    services.Remove(services.Single(service => service.ImplementationType == typeof(AzureSignToolSigner)));
                    services.AddSingleton(payloadSigner);
                    services.AddSingleton(certificates);
                    services.AddSingleton(signatures);
                    services.AddSingleton(manifestSigner);
                    services.AddSingleton(_mage);
                    services.AddSingleton<ILogger<IDataFormatSigner>>(Logger);
                    services.AddSingleton<IDirectoryService>(provider => new ObservingDirectoryService(
                        new DirectoryService(provider.GetRequiredService<ILogger<IDirectoryService>>()),
                        phase => WorkScope?.Invoke(phase)));
                });
                IDirectoryService directories = _services.GetRequiredService<IDirectoryService>();
                _directory = new(directories);
                Coordinator = new(directories);
                Pipeline = new(_services, reader);
            }

            internal Task ExecuteAsync(FileInfo input, FileInfo? output = null,
                ClickOnceApplicationSigningPolicy policy = ClickOnceApplicationSigningPolicy.SignPayloads,
                bool updateManifest = true) =>
                Pipeline.ExecuteAsync(new SigningOperationPlan(SigningFile.Capture(input), output ?? input),
                    Options, Coordinator, policy, updateManifest);

            internal FileInfo Output(string name) => new(Path.Combine(Source.FullName, "output", name));

            internal FileInfo File(string path, string contents)
            {
                FileInfo file = new(Path.Combine(Source.FullName, path));
                file.Directory!.Create();
                System.IO.File.WriteAllText(file.FullName, contents);
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

            internal static DeployManifest NewDeployment()
            {
                DeployManifest manifest = new();
                SetIdentity(manifest);
                return manifest;
            }

            internal FileInfo Deployment(string path, string application, bool mapped = false)
            {
                DeployManifest manifest = NewDeployment();
                manifest.MapFileExtensions = mapped;
                AssemblyReference reference = new(application);
                manifest.AssemblyReferences.Add(reference);
                manifest.EntryPoint = reference;
                return Write(path, manifest);
            }

            internal FileInfo Write(string path, Manifest manifest)
            {
                FileInfo file = new(Path.Combine(Source.FullName, path));
                file.Directory!.Create();
                using FileStream stream = file.Create();
                ManifestWriter.WriteManifest(manifest, stream);
                return file;
            }

            private static void SetIdentity(Manifest manifest)
            {
                manifest.AssemblyIdentity.Name = "Test";
                manifest.AssemblyIdentity.Version = "1.0.0.0";
                manifest.AssemblyIdentity.ProcessorArchitecture = "msil";
            }

            internal void AssertHash(FileInfo deployment, FileInfo application)
            {
                using FileStream stream = deployment.OpenRead();
                Assert.True(new ClickOnceManifestReader().TryReadDeployManifest(stream, out IDeployManifest? manifest));
                using (manifest)
                {
                    Assert.NotNull(manifest);
                    byte[] applicationBytes = System.IO.File.ReadAllBytes(application.FullName);
                    Assert.Equal(applicationBytes.Length, manifest.EntryPoint!.Size);
                    Assert.Equal(Convert.ToBase64String(SHA256.HashData(applicationBytes)), manifest.EntryPoint.Hash);
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

            internal void AssertPayloadHash(FileInfo application, FileInfo payload)
            {
                using FileStream stream = application.OpenRead();
                Assert.True(new ClickOnceManifestReader().TryReadApplicationManifest(stream, out IApplicationManifest? manifest));
                using (manifest)
                {
                    Assert.NotNull(manifest);
                    FileReference reference = Assert.Single(manifest.FileReferences.Cast<FileReference>(),
                        reference => reference.TargetPath == payload.Name);
                    byte[] bytes = System.IO.File.ReadAllBytes(payload.FullName);
                    Assert.Equal(bytes.Length, reference.Size);
                    Assert.Equal(Convert.ToBase64String(SHA256.HashData(bytes)), reference.Hash);
                }
            }

            public async ValueTask DisposeAsync()
            {
                await Coordinator.DisposeAsync();
                Assert.DoesNotContain(_mage.ReceivedCalls(),
                    call => call.GetMethodInfo().Name == nameof(IMageCli.RunAsync));
                while (_keys.TryDequeue(out RSA? key))
                {
                    key.Dispose();
                }
                _directory.Dispose();
                _services.Dispose();
            }
        }
    }
}
