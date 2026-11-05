// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed partial class ClickOnceDeploymentManifestSignerTests
    {
        [Theory]
        [InlineData(false, false, ".application")]
        [InlineData(false, true, ".application")]
        [InlineData(true, false, ".application")]
        [InlineData(true, true, ".application")]
        [InlineData(false, true, ".vsto")]
        [InlineData(true, true, ".vsto")]
        public async Task Nested_OriginalSourceCompletesAcyclicGraphAndPublishesOnlySelectedFiles(
            bool deploymentEntry, bool outOfPlace, string extension)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File(@"Other\payload.dll", "original payload");
            ApplicationManifest otherManifest = new();
            otherManifest.AssemblyIdentity.Name = "OtherApplication";
            otherManifest.AssemblyIdentity.Version = "2.3.4.5";
            otherManifest.AssemblyIdentity.ProcessorArchitecture = "msil";
            otherManifest.FileReferences.Add(new FileReference { TargetPath = "payload.dll" });
            FileInfo other = harness.Write(@"Other\Other.manifest", otherManifest);
            FileInfo child = harness.Deployment($"Child{extension}", @"Other\Other.manifest");
            FileInfo app = harness.Application("App.manifest", child.Name);
            FileInfo root = harness.Deployment("Root.application", app.Name);
            harness.File(@"Other\archive\payload.dll", "archive");
            harness.File(@"v2\Other.manifest", "unselected version");
            harness.File("Child.application.bak", "backup");
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            FileInfo entry = deploymentEntry ? root : app;
            FileInfo output = outOfPlace ? harness.Output(entry.Name) : entry;

            await NestedExecuteAsync(harness, entry, output, deploymentEntry)
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(new[] { payload.Name, other.Name, child.Name, app.Name }
                .Concat(deploymentEntry ? new[] { root.Name } : Array.Empty<string>()), harness.Calls.ToArray());
            Assert.Equal("original payload signed", System.IO.File.ReadAllText(
                Path.Combine(output.DirectoryName!, @"Other\payload.dll")));
            FileInfo publishedOther = new(Path.Combine(output.DirectoryName!, @"Other\Other.manifest"));
            FileInfo publishedChild = new(Path.Combine(output.DirectoryName!, child.Name));
            FileInfo publishedApp = new(Path.Combine(output.DirectoryName!, app.Name));
            NestedAssertApplicationHash(publishedOther, payload.Name,
                new FileInfo(Path.Combine(output.DirectoryName!, @"Other\payload.dll")));
            NestedAssertDeploymentHash(harness, publishedChild, publishedOther);
            NestedAssertApplicationHash(publishedApp, child.Name, publishedChild);
            if (deploymentEntry)
            {
                NestedAssertDeploymentHash(harness, output, publishedApp);
            }
            if (outOfPlace)
            {
                NestedAssertUnchanged(harness.Source, originals);
                NestedAssertFiles(output.Directory!, new[] { app.Name, child.Name,
                    @"Other\Other.manifest", @"Other\payload.dll" }
                    .Concat(deploymentEntry ? new[] { root.Name } : Array.Empty<string>()));
            }
            else
            {
                NestedAssertUnchanged(harness.Source, originals, new[] { app.Name, child.Name,
                    @"Other\Other.manifest", @"Other\payload.dll" }
                    .Concat(deploymentEntry ? new[] { root.Name } : Array.Empty<string>()));
            }
            foreach (FileInfo source in new[] { entry, app, child, other, payload }.DistinctBy(file => file.FullName))
            {
                Assert.True(harness.Coordinator.TryGetResult(SigningFile.Capture(source).SourceIdentity,
                    out Task<SigningOperationResult>? result));
                Assert.NotNull(result);
                Assert.True(result.IsCompletedSuccessfully);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Nested_RealSourceCycleFaultsThroughCoordinatorWithoutLegacyDispatch(bool deploymentEntry)
        {
            await using Harness harness = new();
            FileInfo app = harness.Application("App.manifest", "Child.application");
            harness.Deployment("Child.application", app.Name);
            FileInfo root = harness.Deployment("Root.application", app.Name);
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            FileInfo entry = deploymentEntry ? root : app;
            FileInfo output = harness.Output(entry.Name);

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                NestedExecuteAsync(harness, entry, output, deploymentEntry).WaitAsync(TimeSpan.FromSeconds(20)));

            Assert.Equal("A signing operation dependency cycle was detected.", failure.Message);
            Assert.Empty(harness.Calls);
            NestedAssertUnchanged(harness.Source, originals);
            Assert.False(System.IO.File.Exists(output.FullName));
            if (Directory.Exists(output.DirectoryName))
            {
                Assert.Empty(output.Directory!.EnumerateFiles("*", SearchOption.AllDirectories));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Nested_ApplicationManifestPayloadUsesTypedRouteAndFusionRemainsOrdinary(bool deploymentEntry)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File(@"Other\payload.dll", "payload");
            FileInfo nested = harness.Application(@"Other\Other.manifest", "payload.dll");
            FileInfo fusion = harness.File("Fusion.manifest",
                "<assembly xmlns='urn:schemas-microsoft-com:asm.v1' manifestVersion='1.0'>" +
                "<assemblyIdentity name='Fusion' version='1.0.0.0' type='win32'/></assembly>");
            byte[] fusionBytes = System.IO.File.ReadAllBytes(fusion.FullName);
            FileInfo app = harness.Application("App.manifest", @"Other\Other.manifest", fusion.Name);
            FileInfo root = harness.Deployment("Root.application", app.Name);
            FileInfo entry = deploymentEntry ? root : app;
            FileInfo output = harness.Output(entry.Name);

            await NestedExecuteAsync(harness, entry, output, deploymentEntry).WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(new[] { payload.Name, nested.Name, app.Name }
                .Concat(deploymentEntry ? new[] { root.Name } : Array.Empty<string>()), harness.Calls.ToArray());
            Assert.Equal(fusionBytes, System.IO.File.ReadAllBytes(fusion.FullName));
            Assert.False(System.IO.File.Exists(Path.Combine(output.DirectoryName!, fusion.Name)));
            NestedAssertApplicationHash(new FileInfo(Path.Combine(output.DirectoryName!, app.Name)),
                @"Other\Other.manifest", new FileInfo(Path.Combine(output.DirectoryName!, @"Other\Other.manifest")));
            NestedAssertApplicationHash(new FileInfo(Path.Combine(output.DirectoryName!, app.Name)), fusion.Name, fusion);
            NestedAssertFiles(output.Directory!, new[] { app.Name, @"Other\Other.manifest", @"Other\payload.dll" }
                .Concat(deploymentEntry ? new[] { root.Name } : Array.Empty<string>()));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Nested_MappedChildUsesOriginalPayloadAndKeepsFallbackPublication(
            bool fallback, bool outOfPlace)
        {
            await using Harness harness = new();
            string payloadPath = fallback ? "payload.dll.deploy" : @"Other\payload.dll.deploy";
            FileInfo payload = harness.File(payloadPath, "mapped");
            FileInfo other = harness.Application(@"Other\Other.manifest", "payload.dll");
            FileInfo child = harness.Deployment("Child.application", @"Other\Other.manifest", mapped: true);
            System.IO.File.Move(child.FullName, child.FullName + ".deploy");
            FileInfo physicalChild = new(child.FullName + ".deploy");
            FileInfo app = harness.Application("App.manifest", child.Name);
            FileInfo root = harness.Deployment("Root.application", app.Name, mapped: true);
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            FileInfo output = outOfPlace ? harness.Output(root.Name) : root;

            await harness.ExecuteAsync(root, output).WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal("mapped signed", System.IO.File.ReadAllText(Path.Combine(output.DirectoryName!, payloadPath)));
            FileInfo publishedOther = new(Path.Combine(output.DirectoryName!, @"Other\Other.manifest"));
            FileInfo publishedChild = new(Path.Combine(output.DirectoryName!, physicalChild.Name));
            NestedAssertApplicationHash(publishedOther, "payload.dll",
                new FileInfo(Path.Combine(output.DirectoryName!, payloadPath)));
            NestedAssertDeploymentHash(harness, publishedChild, publishedOther);
            NestedAssertApplicationHash(new FileInfo(Path.Combine(output.DirectoryName!, app.Name)), child.Name, publishedChild);
            NestedAssertDeploymentHash(harness, output, new FileInfo(Path.Combine(output.DirectoryName!, app.Name)));
            Assert.False(System.IO.File.Exists(Path.Combine(output.DirectoryName!, child.Name)));
            Assert.True(harness.Coordinator.TryGetResult(SigningFile.Capture(payload).SourceIdentity, out _));
            if (outOfPlace)
            {
                NestedAssertUnchanged(harness.Source, originals);
                NestedAssertFiles(output.Directory!, new[] { root.Name, app.Name, physicalChild.Name,
                    @"Other\Other.manifest", payloadPath });
            }
            else
            {
                NestedAssertUnchanged(harness.Source, originals,
                    new[] { root.Name, app.Name, physicalChild.Name, @"Other\Other.manifest", payloadPath });
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Nested_ContainerSiblingIdentitiesAreRetainedAcrossEveryEdge(
            bool deploymentEntry, bool nestedContainer)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File(@"Other\payload.dll", "payload");
            FileInfo other = harness.Application(@"Other\Other.manifest", payload.Name);
            FileInfo child = harness.Deployment("Child.application", @"Other\Other.manifest");
            FileInfo app = harness.Application("App.manifest", child.Name);
            FileInfo root = harness.Deployment("Root.application", app.Name);
            FileInfo archive = harness.File("outer.zip", "container identity only");
            SigningSourceIdentity container = SigningSourceIdentity.Capture(archive);
            if (nestedContainer)
            {
                container = SigningSourceIdentity.ContainerEntry(container, "inner.zip");
            }
            FileInfo entry = deploymentEntry ? root : app;
            SigningFile input = SigningFile.ContainerEntry(entry, container, $"publish/{entry.Name}");
            SigningOperationPlan plan = new(input, harness.Output(entry.Name));

            Task operation = deploymentEntry
                ? harness.Pipeline.ExecuteAsync(plan, harness.Options, harness.Coordinator)
                : harness.Pipeline.ExecuteApplicationAsync(plan, harness.Options, harness.Coordinator);
            await operation.WaitAsync(TimeSpan.FromSeconds(20));

            foreach (FileInfo source in new[] { app, child, other, payload })
            {
                SigningSourceIdentity expected = input.GetSibling(source, harness.Source).SourceIdentity;
                Assert.True(harness.Coordinator.TryGetResult(expected, out Task<SigningOperationResult>? result));
                Assert.True(result!.IsCompletedSuccessfully);
                Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(source).SourceIdentity, out _));
            }
            Assert.Equal(1, harness.Calls.Count(name => name == payload.Name));
        }

        [Theory]
        [InlineData(false, false, 1)]
        [InlineData(false, true, 1)]
        [InlineData(true, false, 1)]
        [InlineData(true, true, 1)]
        [InlineData(true, true, 4)]
        public async Task Nested_SharedDiamondAndExplicitImplicitOverlapSignEachIdentityOnce(
            bool explicitPrerequisites, bool reverse, int maxConcurrency)
        {
            await using Harness harness = new();
            ConcurrentQueue<string> payloadCalls = NestedObservePayloads(harness);
            FileInfo payload = harness.File(@"Other\payload.dll", "payload");
            FileInfo other = harness.Application(@"Other\Other.manifest", payload.Name);
            FileInfo left = harness.Deployment("Left.application", @"Other\Other.manifest");
            FileInfo right = harness.Deployment("Right.vsto", @"Other\Other.manifest");
            FileInfo app = harness.Application("App.manifest", left.Name, right.Name);
            FileInfo root = harness.Deployment("Root.application", app.Name);
            List<SigningOperationPlan> plans = new()
            {
                new SigningOperationPlan(SigningFile.Capture(root), harness.Output(root.Name))
            };
            if (explicitPrerequisites)
            {
                foreach (FileInfo file in new[] { app, left, right, other, payload })
                {
                    plans.Add(new SigningOperationPlan(SigningFile.Capture(file),
                        harness.Output(Path.GetRelativePath(harness.Source.FullName, file.FullName))));
                }
            }
            if (reverse)
            {
                plans.Reverse();
            }

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency)
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(6, harness.Calls.Count);
            Assert.All(new[] { other, left, right, app, root },
                file => Assert.Equal(1, harness.Calls.Count(name => name == file.Name)));
            Assert.Equal(new[] { "payload" }, payloadCalls.ToArray());
            FileInfo publishedOther = harness.Output(@"Other\Other.manifest");
            NestedAssertDeploymentHash(harness, harness.Output(left.Name), publishedOther);
            NestedAssertDeploymentHash(harness, harness.Output(right.Name), publishedOther);
            NestedAssertApplicationHash(harness.Output(app.Name), left.Name, harness.Output(left.Name));
            NestedAssertApplicationHash(harness.Output(app.Name), right.Name, harness.Output(right.Name));
            NestedAssertDeploymentHash(harness, harness.Output(root.Name), harness.Output(app.Name));
            NestedAssertFiles(harness.Output(root.Name).Directory!, new[] { root.Name, app.Name, left.Name,
                right.Name, @"Other\Other.manifest", @"Other\payload.dll" });
            Assert.Equal("payload", System.IO.File.ReadAllText(payload.FullName));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Nested_DisabledDependencyModesDoNotTraverseSignOrPublishImplicitGraph(
            bool deploymentEntry, bool noUpdate)
        {
            await using Harness harness = new();
            FileInfo child = harness.Deployment("Child.application", @"missing\Other.manifest");
            FileInfo app = harness.Application("App.manifest", child.Name);
            FileInfo root = harness.Deployment("Root.application", app.Name);
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            FileInfo entry = deploymentEntry ? root : app;
            FileInfo output = harness.Output(entry.Name);
            ClickOnceApplicationSigningPolicy policy = noUpdate
                ? ClickOnceApplicationSigningPolicy.SignPayloads
                : ClickOnceApplicationSigningPolicy.UpdateManifestOnly;

            await NestedExecuteAsync(harness, entry, output, deploymentEntry, policy, updateManifest: !noUpdate)
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(new[] { entry.Name }, harness.Calls.ToArray());
            NestedAssertFiles(output.Directory!, new[] { entry.Name });
            NestedAssertUnchanged(harness.Source, originals);
            Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(child).SourceIdentity, out _));
            if (noUpdate)
            {
                Assert.Equal(System.IO.File.ReadAllText(entry.FullName) + "\n<!--signed-->",
                    System.IO.File.ReadAllText(output.FullName));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Nested_DisabledDependencyModesPreserveIndependentlySelectedOutputs(bool noUpdate)
        {
            await using Harness harness = new();
            ConcurrentQueue<string> payloadCalls = NestedObservePayloads(harness);
            FileInfo payload = harness.File(@"Other\payload.dll", "payload");
            harness.Application(@"Other\Other.manifest", payload.Name);
            FileInfo child = harness.Deployment("Child.application", @"Other\Other.manifest");
            FileInfo app = harness.Application("App.manifest", child.Name);
            FileInfo root = harness.Deployment("Root.application", app.Name);
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            SigningOperationPlan[] plans = new[]
            {
                new SigningOperationPlan(SigningFile.Capture(root), harness.Output(@"parent\Root.application")),
                new SigningOperationPlan(SigningFile.Capture(app), harness.Output(@"explicit\App.manifest")),
                new SigningOperationPlan(SigningFile.Capture(child), harness.Output(@"independent\Child.application")),
                new SigningOperationPlan(SigningFile.Capture(payload), harness.Output(@"payload\payload.dll"))
            };

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 1,
                noUpdate ? ClickOnceApplicationSigningPolicy.SignPayloads : ClickOnceApplicationSigningPolicy.UpdateManifestOnly,
                updateManifest: !noUpdate).WaitAsync(TimeSpan.FromSeconds(20));

            Assert.All(new[] { root, app, child },
                file => Assert.Equal(1, harness.Calls.Count(name => name == file.Name)));
            Assert.Equal(new[] { "payload" }, payloadCalls.ToArray());
            Assert.Equal(4, harness.Calls.Count);
            Assert.Equal("payload signed", System.IO.File.ReadAllText(harness.Output(@"payload\payload.dll").FullName));
            NestedAssertFiles(harness.Output("unused").Directory!, new[] { @"parent\Root.application",
                @"explicit\App.manifest", @"independent\Child.application", @"payload\payload.dll" });
            NestedAssertUnchanged(harness.Source, originals);
        }

        [Theory]
        [InlineData("App.manifest")]
        [InlineData("direct.dll")]
        [InlineData("Child.application")]
        public async Task Nested_CompanionWithDifferentSourceCannotReplaceReservedOutput(string companionTarget)
        {
            await using Harness harness = new();
            harness.File("direct.dll", "direct");
            harness.File("Child.application", "child");
            FileInfo app = harness.Application("App.manifest", "Child.application", "direct.dll");
            FileInfo signedChild = harness.File(@"results\child.bin", "signed child");
            FileInfo different = harness.File(@"results\different.bin", "different");
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            handler.CanSign(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>())
                .Returns(call => call.Arg<ClickOncePayloadSigningRequest>().Source.File.Name == "Child.application");
            handler.SignAsync(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>(),
                Arg.Any<SigningOperationCoordinator>()).Returns(async call =>
                {
                    ClickOncePayloadSigningRequest request = call.Arg<ClickOncePayloadSigningRequest>();
                    SigningOperationResult childResult = await harness.Coordinator.ExecuteAsync(
                        request.Source.SourceIdentity, () => Task.FromResult(signedChild));
                    SigningOperationResult differentResult = await harness.Coordinator.ExecuteAsync(
                        SigningFile.Capture(different).SourceIdentity, () => Task.FromResult(different));
                    SigningOperationPlan companion = new(SigningFile.Capture(different),
                        new FileInfo(Path.Combine(request.StagingRoot.FullName, companionTarget)));
                    return new ClickOncePayloadSigningResult(childResult,
                        new[] { new ClickOncePayloadSigningOutput(companion, differentResult) });
                });
            ClickOnceApplicationManifestSigner pipeline = harness.NestedApplicationPipeline(handler);
            FileInfo output = harness.Output(app.Name);

            await Assert.ThrowsAsync<ClickOnceStagingException>(() => pipeline.ExecuteAsync(
                new SigningOperationPlan(SigningFile.Capture(app), output), harness.Options, harness.Coordinator,
                ClickOnceApplicationSigningPolicy.SignPayloads).WaitAsync(TimeSpan.FromSeconds(20)));

            NestedAssertUnchanged(harness.Source, originals);
            Assert.DoesNotContain(app.Name, harness.Calls);
            Assert.False(System.IO.File.Exists(output.FullName));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Nested_RedundantCompanionForSameDirectSourceAndResultIsOmitted(bool duplicateDirectRoute)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File(@"Other\payload.dll", "payload");
            FileInfo other = harness.Application(@"Other\Other.manifest", payload.Name);
            FileInfo child = harness.Deployment("Child.application", @"Other\Other.manifest");
            FileInfo app = duplicateDirectRoute
                ? harness.Application("App.manifest", child.Name, child.Name, @"Other\Other.manifest")
                : harness.Application("App.manifest", child.Name, @"Other\Other.manifest");
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            FileInfo output = harness.Output(app.Name);

            await harness.Pipeline.ExecuteApplicationAsync(new SigningOperationPlan(SigningFile.Capture(app), output),
                harness.Options, harness.Coordinator)
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(new[] { payload.Name, other.Name, child.Name, app.Name }, harness.Calls.ToArray());
            NestedAssertUnchanged(harness.Source, originals);
            NestedAssertFiles(output.Directory!, new[] { app.Name, child.Name, @"Other\Other.manifest", @"Other\payload.dll" });
            NestedAssertApplicationHash(output, child.Name, harness.Output(child.Name),
                expectedReferences: duplicateDirectRoute ? 2 : 1);
            NestedAssertApplicationHash(output, @"Other\Other.manifest", harness.Output(@"Other\Other.manifest"));
            NestedAssertDeploymentHash(harness, harness.Output(child.Name), harness.Output(@"Other\Other.manifest"));
            NestedAssertApplicationHash(harness.Output(@"Other\Other.manifest"), payload.Name, harness.Output(@"Other\payload.dll"));
        }

        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 1)]
        [InlineData(false, 4)]
        [InlineData(true, 4)]
        public async Task Nested_Issue681_DistinctApplicationAndVstoPairsShareDirectoryWithoutCrossBinding(
            bool reverse, int maxConcurrency)
        {
            await using Harness harness = new();
            FileInfo firstPayload = harness.File("First.dll", "first payload");
            FileInfo secondPayload = harness.File("Second.dll", "second payload");
            FileInfo thirdPayload = harness.File("Third.dll", "third payload");
            FileInfo firstApp = harness.Application("First.manifest", firstPayload.Name);
            FileInfo secondApp = harness.Application("Second.manifest", secondPayload.Name);
            FileInfo thirdApp = harness.Application("Third.manifest", thirdPayload.Name);
            FileInfo first = harness.Deployment("First.application", firstApp.Name);
            FileInfo second = harness.Deployment("Second.vsto", secondApp.Name);
            FileInfo third = harness.Deployment("Third.application", thirdApp.Name);
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            List<SigningOperationPlan> plans = new[] { first, second, third }
                .Select(file => new SigningOperationPlan(SigningFile.Capture(file), harness.Output(file.Name))).ToList();
            if (reverse)
            {
                plans.Reverse();
            }

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency)
                .WaitAsync(TimeSpan.FromSeconds(20));

            (FileInfo Deployment, FileInfo Application, FileInfo Payload, string Contents)[] pairs = new[]
            {
                (first, firstApp, firstPayload, "first payload signed"),
                (second, secondApp, secondPayload, "second payload signed"),
                (third, thirdApp, thirdPayload, "third payload signed")
            };
            foreach ((FileInfo deployment, FileInfo application, FileInfo payload, string contents) in pairs)
            {
                NestedAssertDeploymentHash(harness, harness.Output(deployment.Name), harness.Output(application.Name));
                NestedAssertApplicationHash(harness.Output(application.Name), payload.Name, harness.Output(payload.Name));
                Assert.Equal(contents, System.IO.File.ReadAllText(harness.Output(payload.Name).FullName));
                Assert.Equal(1, harness.Calls.Count(name => name == deployment.Name));
                Assert.Equal(1, harness.Calls.Count(name => name == application.Name));
                Assert.Equal(1, harness.Calls.Count(name => name == payload.Name));
            }
            Assert.Equal(9, harness.Calls.Count);
            NestedAssertFiles(harness.Output(first.Name).Directory!,
                pairs.SelectMany(pair => new[] { pair.Deployment.Name, pair.Application.Name, pair.Payload.Name }));
            NestedAssertUnchanged(harness.Source, originals);
        }

        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 1)]
        [InlineData(false, 4)]
        [InlineData(true, 4)]
        public async Task Nested_Issue760_SelectedOlderVersionAndExplicitOtherVersionKeepSeparateGraphs(
            bool reverse, int maxConcurrency)
        {
            await using Harness harness = new();
            ConcurrentQueue<string> payloadCalls = NestedObservePayloads(harness);
            FileInfo olderPayload = harness.File(@"v1\payload.dll", "older payload");
            FileInfo newerPayload = harness.File(@"v2\payload.dll", "newer payload");
            FileInfo olderApp = harness.Application(@"v1\App.manifest", olderPayload.Name);
            FileInfo newerApp = harness.Application(@"v2\Other.manifest", newerPayload.Name);
            harness.File(@"v3\unselected.dll", "latest unselected");
            harness.Application(@"v3\Latest.manifest", "unselected.dll");
            harness.File(@"v1\archive\App.manifest", "archive");
            harness.File(@"v2\Other.manifest.bak", "backup");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest");
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            List<SigningOperationPlan> plans = new()
            {
                new SigningOperationPlan(SigningFile.Capture(deployment), harness.Output(deployment.Name)),
                new SigningOperationPlan(SigningFile.Capture(newerApp), harness.Output(@"v2\Other.manifest")),
                new SigningOperationPlan(SigningFile.Capture(newerPayload), harness.Output(@"v2\payload.dll"))
            };
            if (reverse)
            {
                plans.Reverse();
            }

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency)
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal("older payload signed", System.IO.File.ReadAllText(harness.Output(@"v1\payload.dll").FullName));
            Assert.Equal("newer payload signed", System.IO.File.ReadAllText(harness.Output(@"v2\payload.dll").FullName));
            NestedAssertDeploymentHash(harness, harness.Output(deployment.Name), harness.Output(@"v1\App.manifest"));
            NestedAssertApplicationHash(harness.Output(@"v1\App.manifest"), olderPayload.Name, harness.Output(@"v1\payload.dll"));
            NestedAssertApplicationHash(harness.Output(@"v2\Other.manifest"), newerPayload.Name, harness.Output(@"v2\payload.dll"));
            using FileStream stream = harness.Output(deployment.Name).OpenRead();
            Assert.True(new ClickOnceManifestReader().TryReadDeployManifest(stream, out IDeployManifest? updated));
            using (updated)
            {
                Assert.Equal(@"v1\App.manifest", updated!.EntryPoint!.TargetPath);
            }
            Assert.Equal(5, harness.Calls.Count);
            Assert.Equal(new[] { "newer payload", "older payload" },
                payloadCalls.OrderBy(contents => contents, StringComparer.Ordinal));
            Assert.Equal(1, harness.Calls.Count(name => name == olderApp.Name));
            Assert.Equal(1, harness.Calls.Count(name => name == newerApp.Name));
            NestedAssertFiles(harness.Output(deployment.Name).Directory!,
                new[] { deployment.Name, @"v1\App.manifest", @"v1\payload.dll", @"v2\Other.manifest", @"v2\payload.dll" });
            NestedAssertUnchanged(harness.Source, originals);
        }

        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 1)]
        [InlineData(false, 4)]
        [InlineData(true, 4)]
        public async Task Nested_Issue1065_BroadExplicitSelectionPublishesFinalHashChainOnceOutOfPlace(
            bool reverse, int maxConcurrency)
        {
            await using Harness harness = new();
            ConcurrentQueue<string> payloadCalls = NestedObservePayloads(harness);
            FileInfo nestedPayload = harness.File(@"Other\payload.dll", "nested payload");
            FileInfo directPayload = harness.File("direct.dll", "direct payload");
            FileInfo other = harness.Application(@"Other\Other.manifest", nestedPayload.Name);
            FileInfo child = harness.Deployment("Child.vsto", @"Other\Other.manifest");
            FileInfo app = harness.Application("App.manifest", child.Name, directPayload.Name);
            FileInfo root = harness.Deployment("Root.application", app.Name);
            FileInfo[] selected = new[] { nestedPayload, directPayload, other, child, app, root };
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            List<SigningOperationPlan> plans = selected.Select(file =>
                new SigningOperationPlan(SigningFile.Capture(file),
                    harness.Output(Path.GetRelativePath(harness.Source.FullName, file.FullName)))).ToList();
            if (reverse)
            {
                plans.Reverse();
            }

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency)
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(selected.Length, harness.Calls.Count);
            Assert.All(new[] { other, child, app, root },
                file => Assert.Equal(1, harness.Calls.Count(name => name == file.Name)));
            Assert.Equal(new[] { "direct payload", "nested payload" },
                payloadCalls.OrderBy(contents => contents, StringComparer.Ordinal));
            Assert.Equal("nested payload signed", System.IO.File.ReadAllText(harness.Output(@"Other\payload.dll").FullName));
            Assert.Equal("direct payload signed", System.IO.File.ReadAllText(harness.Output(directPayload.Name).FullName));
            NestedAssertApplicationHash(harness.Output(@"Other\Other.manifest"), nestedPayload.Name,
                harness.Output(@"Other\payload.dll"));
            NestedAssertDeploymentHash(harness, harness.Output(child.Name), harness.Output(@"Other\Other.manifest"));
            NestedAssertApplicationHash(harness.Output(app.Name), child.Name, harness.Output(child.Name));
            NestedAssertApplicationHash(harness.Output(app.Name), directPayload.Name, harness.Output(directPayload.Name));
            NestedAssertDeploymentHash(harness, harness.Output(root.Name), harness.Output(app.Name));
            NestedAssertFiles(harness.Output(root.Name).Directory!, selected.Select(file =>
                Path.GetRelativePath(harness.Source.FullName, file.FullName)));
            NestedAssertUnchanged(harness.Source, originals);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Nested_ZipRoundtripUsesExtractedOriginalIdentitiesAndRebuildsOnlySelectedEntries(
            bool deploymentEntry)
        {
            await using Harness harness = new();
            harness.File(@"publish\Other\payload.dll", "archive payload");
            harness.Application(@"publish\Other\Other.manifest", "payload.dll");
            harness.Deployment(@"publish\Child.vsto", @"Other\Other.manifest");
            harness.Application(@"publish\App.manifest", "Child.vsto");
            harness.Deployment(@"publish\Root.application", "App.manifest");
            harness.File(@"publish\v2\unselected.dll", "unselected version");
            harness.File(@"publish\archive\Other.manifest", "archive");
            harness.File(@"publish\Child.vsto.bak", "backup");
            DirectoryInfo publish = new(Path.Combine(harness.Source.FullName, "publish"));
            FileInfo archive = new(Path.Combine(harness.Source.FullName, "source.zip"));
            ZipFile.CreateFromDirectory(publish.FullName, archive.FullName, CompressionLevel.Optimal,
                includeBaseDirectory: true);
            byte[] originalArchive = System.IO.File.ReadAllBytes(archive.FullName);
            DirectoryInfo extracted = new(Path.Combine(harness.Source.FullName, "extracted"));
            ZipFile.ExtractToDirectory(archive.FullName, extracted.FullName);
            DirectoryInfo extractedPublish = new(Path.Combine(extracted.FullName, "publish"));
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            string entryName = deploymentEntry ? "Root.application" : "App.manifest";
            FileInfo entry = new(Path.Combine(extractedPublish.FullName, entryName));
            SigningFile input = SigningFile.ContainerEntry(entry, SigningSourceIdentity.Capture(archive),
                $"publish/{entryName}");
            FileInfo output = harness.Output(Path.Combine("roundtrip", "publish", entryName));
            SigningOperationPlan plan = new(input, output);

            Task operation = deploymentEntry
                ? harness.Pipeline.ExecuteAsync(plan, harness.Options, harness.Coordinator)
                : harness.Pipeline.ExecuteApplicationAsync(plan, harness.Options, harness.Coordinator);
            await operation.WaitAsync(TimeSpan.FromSeconds(20));

            string[] selected = new[] { "App.manifest", "Child.vsto", @"Other\Other.manifest", @"Other\payload.dll" }
                .Concat(deploymentEntry ? new[] { "Root.application" } : Array.Empty<string>()).ToArray();
            NestedAssertFiles(output.Directory!, selected);
            foreach (string relativePath in selected)
            {
                FileInfo source = new(Path.Combine(extractedPublish.FullName, relativePath));
                SigningSourceIdentity identity = input.GetSibling(source, extractedPublish).SourceIdentity;
                Assert.True(harness.Coordinator.TryGetResult(identity, out Task<SigningOperationResult>? result));
                Assert.True(result!.IsCompletedSuccessfully);
                Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(source).SourceIdentity, out _));
            }
            Assert.Equal(new[] { "payload.dll", "Other.manifest", "Child.vsto", "App.manifest" }
                .Concat(deploymentEntry ? new[] { "Root.application" } : Array.Empty<string>()),
                harness.Calls.ToArray());
            FileInfo rebuilt = harness.Output(@"roundtrip\signed.zip");
            ZipFile.CreateFromDirectory(output.DirectoryName!, rebuilt.FullName, CompressionLevel.Optimal,
                includeBaseDirectory: true);
            using (ZipArchive rebuiltArchive = ZipFile.OpenRead(rebuilt.FullName))
            {
                Assert.Equal(selected.Select(path => "publish/" + path.Replace('\\', '/'))
                    .OrderBy(path => path, StringComparer.Ordinal),
                    rebuiltArchive.Entries.Select(zipEntry => zipEntry.FullName)
                        .OrderBy(path => path, StringComparer.Ordinal));
            }
            DirectoryInfo verification = new(Path.Combine(rebuilt.DirectoryName!, "verification"));
            ZipFile.ExtractToDirectory(rebuilt.FullName, verification.FullName);
            NestedAssertFiles(verification, selected.Select(path => Path.Combine("publish", path)));
            DirectoryInfo verifiedPublish = new(Path.Combine(verification.FullName, "publish"));
            FileInfo verifiedPayload = new(Path.Combine(verifiedPublish.FullName, @"Other\payload.dll"));
            FileInfo verifiedOther = new(Path.Combine(verifiedPublish.FullName, @"Other\Other.manifest"));
            FileInfo verifiedChild = new(Path.Combine(verifiedPublish.FullName, "Child.vsto"));
            FileInfo verifiedApp = new(Path.Combine(verifiedPublish.FullName, "App.manifest"));
            Assert.Equal("archive payload signed", System.IO.File.ReadAllText(verifiedPayload.FullName));
            NestedAssertApplicationHash(verifiedOther, verifiedPayload.Name, verifiedPayload);
            NestedAssertDeploymentHash(harness, verifiedChild, verifiedOther);
            NestedAssertApplicationHash(verifiedApp, verifiedChild.Name, verifiedChild);
            if (deploymentEntry)
            {
                NestedAssertDeploymentHash(harness,
                    new FileInfo(Path.Combine(verifiedPublish.FullName, "Root.application")), verifiedApp);
            }
            Assert.Equal(originalArchive, System.IO.File.ReadAllBytes(archive.FullName));
            NestedAssertUnchanged(harness.Source, originals);
        }

        [Fact]
        public async Task Nested_NativeVstoSigningPreservesApplicationAndDeploymentSubtreesWithoutArchivalCopies()
        {
            AppInitializer.Initialize();
            await using Harness harness = new(realSigning: true);
            FileInfo vstoApplication = harness.File(@"Nested\AddIn.manifest", System.IO.File.ReadAllText(
                ClickOnceResolutionTestUtilities.GetVstoManifestPath()));
            FileInfo payload = new(Path.Combine(harness.Source.FullName, @"Nested\payload.dll"));
            System.IO.File.Copy(typeof(ClickOnceDeploymentManifestSignerTests).Assembly.Location, payload.FullName);
            FileInfo vstoDeployment = harness.Deployment(@"Nested\AddIn.vsto", vstoApplication.Name);
            XDocument deploymentDocument = XDocument.Load(vstoDeployment.FullName);
            XElement deploymentSubtree = XElement.Parse(
                "<vsta:customization xmlns:vsta='urn:schemas-microsoft-com:vsta.v3' " +
                "xmlns:vsto='urn:schemas-microsoft-com:vsto.v4'>" +
                "<vsto:appAddIn keyName='KeepNestedCustomization'/></vsta:customization>");
            deploymentDocument.Root!.Add(deploymentSubtree);
            deploymentDocument.Save(vstoDeployment.FullName);
            XElement applicationSubtree = XDocument.Load(vstoApplication.FullName).Descendants()
                .Single(element => element.Name == XName.Get("addIn", "urn:schemas-microsoft-com:vsta.v3"));
            FileInfo app = harness.Application("App.manifest", @"Nested\AddIn.vsto");
            FileInfo root = harness.Deployment("Root.application", app.Name);
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            FileInfo output = harness.Output(root.Name);

            await harness.ExecuteAsync(root, output).WaitAsync(TimeSpan.FromSeconds(30));

            FileInfo publishedApplication = harness.Output(@"Nested\AddIn.manifest");
            FileInfo publishedDeployment = harness.Output(@"Nested\AddIn.vsto");
            FileInfo publishedPayload = harness.Output(@"Nested\payload.dll");
            Assert.True(XNode.DeepEquals(applicationSubtree, XDocument.Load(publishedApplication.FullName)
                .Descendants().Single(element => element.Name == applicationSubtree.Name)));
            Assert.True(XNode.DeepEquals(deploymentSubtree, XDocument.Load(publishedDeployment.FullName)
                .Descendants().Single(element => element.Name == deploymentSubtree.Name)));
            foreach (FileInfo manifest in new[] { publishedApplication, publishedDeployment,
                harness.Output(app.Name), output })
            {
                XDocument signedDocument = XDocument.Load(manifest.FullName);
                Assert.Contains(signedDocument.Descendants(),
                    element => element.Name == XName.Get("Signature", "http://www.w3.org/2000/09/xmldsig#"));
                Assert.DoesNotContain(signedDocument.Descendants(),
                    element => element.Name == XName.Get("Signature", "http://www.w3.org/2000/09/xmldsig#") &&
                        (string?)element.Attribute("Id") == "OldSignature");
            }
            using (FileStream stream = publishedApplication.OpenRead())
            {
                Assert.True(new ClickOnceManifestReader().TryReadApplicationManifest(stream,
                    out IApplicationManifest? updated));
                using (updated)
                {
                    AssemblyReference reference = Assert.Single(updated!.AssemblyReferences.Cast<AssemblyReference>(),
                        assembly => assembly.TargetPath == payload.Name);
                    byte[] bytes = System.IO.File.ReadAllBytes(publishedPayload.FullName);
                    Assert.Equal(bytes.Length, reference.Size);
                    Assert.Equal(Convert.ToBase64String(SHA256.HashData(bytes)), reference.Hash);
                }
            }
            NestedAssertDeploymentHash(harness, publishedDeployment, publishedApplication);
            NestedAssertApplicationHash(harness.Output(app.Name), @"Nested\AddIn.vsto", publishedDeployment);
            NestedAssertDeploymentHash(harness, output, harness.Output(app.Name));
            Assert.Equal(new[] { payload.Name, vstoApplication.Name, vstoDeployment.Name, app.Name, root.Name },
                harness.Calls.ToArray());
            NestedAssertFiles(output.Directory!, new[] { root.Name, app.Name, @"Nested\AddIn.vsto",
                @"Nested\AddIn.manifest", @"Nested\payload.dll" });
            NestedAssertUnchanged(harness.Source, originals);
        }

        [Theory]
        [InlineData(false, false, false, 1)]
        [InlineData(false, false, true, 1)]
        [InlineData(true, false, false, 1)]
        [InlineData(true, true, false, 1)]
        [InlineData(true, false, true, 1)]
        [InlineData(true, true, true, 1)]
        [InlineData(false, false, true, 4)]
        [InlineData(true, true, true, 4)]
        public Task Nested_ExplicitApplicationContextPreservesDeploymentRootFallbackParity(
            bool explicitApplication, bool reverse, bool outOfPlace, int maxConcurrency) =>
            NestedVerifyApplicationContextAsync(explicitApplication, reverse, outOfPlace, maxConcurrency,
                mapped: false, container: false);

        [Theory]
        [InlineData(false, false, false, 1)]
        [InlineData(false, false, true, 1)]
        [InlineData(true, false, false, 1)]
        [InlineData(true, true, false, 1)]
        [InlineData(true, false, true, 1)]
        [InlineData(true, true, true, 1)]
        [InlineData(false, false, true, 4)]
        [InlineData(true, true, true, 4)]
        public Task Nested_ExplicitApplicationContextPreservesMappedSuffixPreferenceParity(
            bool explicitApplication, bool reverse, bool outOfPlace, int maxConcurrency) =>
            NestedVerifyApplicationContextAsync(explicitApplication, reverse, outOfPlace, maxConcurrency,
                mapped: true, container: false);

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public Task Nested_ExplicitApplicationContextPreservesContainerSiblingProvenance(bool mapped, bool reverse) =>
            NestedVerifyApplicationContextAsync(explicitApplication: true, reverse, outOfPlace: true, maxConcurrency: 1,
                mapped, container: true);

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public Task Nested_NoSignExplicitApplicationUsesDeploymentContextWithoutInheritingPayloadOutputs(
            bool mapped, bool reverse) =>
            NestedVerifyApplicationContextAsync(explicitApplication: true, reverse, outOfPlace: true, maxConcurrency: 1,
                mapped, container: false, noSign: true);

        [Theory]
        [InlineData(false, false, false, 1)]
        [InlineData(false, true, true, 1)]
        [InlineData(true, false, true, 4)]
        [InlineData(true, true, true, 1)]
        public async Task Nested_CompatibleDeploymentApplicationContextsShareOneOwnerAndResult(
            bool mapped, bool reverse, bool explicitApplication, int maxConcurrency)
        {
            await using Harness harness = new();
            ConcurrentQueue<string> payloadCalls = NestedObservePayloads(harness);
            string payloadPath = mapped ? "payload.dll.deploy" : "payload.dll";
            FileInfo payload = harness.File(payloadPath, "shared context payload");
            if (mapped)
            {
                harness.File("payload.dll", "unselected unsuffixed payload");
            }
            FileInfo app = harness.Application(@"v1\App.manifest", "payload.dll");
            FileInfo first = harness.Deployment("Alpha.application", @"v1\App.manifest", mapped);
            FileInfo second = harness.Deployment("Beta.vsto", @"v1\App.manifest", mapped);
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            List<SigningOperationPlan> plans = new()
            {
                new SigningOperationPlan(SigningFile.Capture(first), harness.Output(first.Name)),
                new SigningOperationPlan(SigningFile.Capture(second), harness.Output(second.Name))
            };
            if (explicitApplication)
            {
                plans.Add(new SigningOperationPlan(SigningFile.Capture(app), harness.Output(@"v1\App.manifest")));
            }
            if (reverse)
            {
                plans.Reverse();
            }

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency)
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(4, harness.Calls.Count);
            Assert.Equal(new[] { "shared context payload" }, payloadCalls.ToArray());
            Assert.All(new[] { app, first, second },
                file => Assert.Equal(1, harness.Calls.Count(name => name == file.Name)));
            FileInfo publishedApp = harness.Output(@"v1\App.manifest");
            FileInfo publishedPayload = harness.Output(payloadPath);
            Assert.Equal("shared context payload signed", System.IO.File.ReadAllText(publishedPayload.FullName));
            NestedAssertApplicationHash(publishedApp, "payload.dll", publishedPayload);
            NestedAssertDeploymentHash(harness, harness.Output(first.Name), publishedApp);
            NestedAssertDeploymentHash(harness, harness.Output(second.Name), publishedApp);
            SigningSourceIdentity applicationIdentity = SigningFile.Capture(app).SourceIdentity;
            Assert.True(harness.Coordinator.TryGetResult(applicationIdentity, out Task<SigningOperationResult>? shared));
            Task<SigningOperationResult> duplicate = harness.Coordinator.ExecuteAsync(applicationIdentity,
                () => throw new InvalidOperationException("A compatible context must not claim a second owner."));
            Assert.Same(shared, duplicate);
            Assert.Same(await shared!, await duplicate);
            Assert.True(harness.Coordinator.TryGetResult(SigningFile.Capture(payload).SourceIdentity, out _));
            NestedAssertFiles(harness.Output(first.Name).Directory!,
                new[] { first.Name, second.Name, @"v1\App.manifest", payloadPath });
            NestedAssertUnchanged(harness.Source, originals);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Nested_IncompatibleMappedAndUnmappedApplicationContextsRejectBeforeAnyMutation(
            bool container, bool reverse)
        {
            await using Harness harness = new();
            harness.File(@"v1\payload.dll", "unmapped payload");
            harness.File(@"v1\payload.dll.deploy", "mapped payload");
            FileInfo ordinary = harness.File("ordinary.dll", "independently selected payload");
            FileInfo app = harness.Application(@"v1\App.manifest", "payload.dll");
            FileInfo unmapped = harness.Deployment("Alpha.application", @"v1\App.manifest");
            FileInfo mapped = harness.Deployment("Beta.vsto", @"v1\App.manifest", mapped: true);
            SigningSourceIdentity? parent = container
                ? SigningSourceIdentity.Capture(harness.File("container.zip", "container identity control"))
                : null;
            SigningFile applicationInput = Input(app);
            List<SigningOperationPlan> plans = new()
            {
                new SigningOperationPlan(Input(unmapped), harness.Output(unmapped.Name)),
                new SigningOperationPlan(Input(mapped), harness.Output(mapped.Name)),
                new SigningOperationPlan(applicationInput, harness.Output(@"v1\App.manifest")),
                new SigningOperationPlan(Input(ordinary), harness.Output(ordinary.Name))
            };
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            if (reverse)
            {
                plans.Reverse();
            }

            ClickOncePublishLayoutResolutionException failure =
                await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(() =>
                    harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 4)
                        .WaitAsync(TimeSpan.FromSeconds(20)));

            string deploymentPaths = string.Join(", ", new[] { unmapped.FullName, mapped.FullName }
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
            string expected = $"The ClickOnce application manifest '{app.FullName}' has incompatible source, " +
                $"destination, or file-mapping contexts in these deployment manifests: {deploymentPaths}.";
            Assert.Equal(expected, failure.Message);
            Assert.Empty(harness.Calls);
            Assert.False(harness.Coordinator.TryGetResult(Input(ordinary).SourceIdentity, out _));
            NestedAssertUnchanged(harness.Source, originals);
            DirectoryInfo output = harness.Output(unmapped.Name).Directory!;
            if (output.Exists)
            {
                Assert.Empty(output.EnumerateFiles("*", SearchOption.AllDirectories));
            }

            SigningFile Input(FileInfo file) => parent is null
                ? SigningFile.Capture(file)
                : SigningFile.ContainerEntry(file, parent,
                    "publish/" + Path.GetRelativePath(harness.Source.FullName, file.FullName).Replace('\\', '/'));
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, true, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, true)]
        public async Task Nested_IncompatibleFallbackSourcesRejectUnmappedSharedApplicationBeforeAnyMutation(
            bool container, bool reverse, bool outOfPlace)
        {
            await using Harness harness = new();
            FileInfo outerPayload = harness.File(@"Root\payload.dll", "outer deployment fallback");
            FileInfo innerPayload = harness.File(@"Root\Origin\payload.dll", "inner deployment fallback");
            FileInfo ordinary = harness.File(@"Root\ordinary.dll", "independently selected payload");
            FileInfo app = harness.Application(@"Root\Origin\v1\App.manifest", "payload.dll");
            FileInfo first = harness.Deployment(@"Root\Origin\First.application", @"v1\App.manifest");
            FileInfo second = harness.Deployment(@"Root\Second.application", @"Origin\v1\App.manifest");
            SigningSourceIdentity? parent = container
                ? SigningSourceIdentity.Capture(harness.File("container.zip", "container identity control"))
                : null;
            SigningFile firstInput = Input(first);
            SigningFile secondInput = Input(second);
            SigningFile applicationInput = Input(app);
            Assert.Equal(applicationInput.SourceIdentity,
                firstInput.GetSibling(app, first.Directory!).SourceIdentity);
            Assert.Equal(applicationInput.SourceIdentity,
                secondInput.GetSibling(app, second.Directory!).SourceIdentity);
            Assert.False(System.IO.File.Exists(Path.Combine(app.DirectoryName!, "payload.dll")));
            Assert.NotEqual(System.IO.File.ReadAllText(outerPayload.FullName),
                System.IO.File.ReadAllText(innerPayload.FullName));
            List<SigningOperationPlan> plans = new[]
            {
                first, second, app, ordinary
            }.Select(file => new SigningOperationPlan(Input(file), Destination(file))).ToList();
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            if (reverse)
            {
                plans.Reverse();
            }

            ClickOncePublishLayoutResolutionException failure =
                await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(() =>
                    harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 4)
                        .WaitAsync(TimeSpan.FromSeconds(20)));

            string deploymentPaths = string.Join(", ", new[] { first.FullName, second.FullName }
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
            string expected = $"The ClickOnce application manifest '{app.FullName}' has incompatible source, " +
                $"destination, or file-mapping contexts in these deployment manifests: {deploymentPaths}.";
            Assert.Equal(expected, failure.Message);
            Assert.Empty(harness.Calls);
            Assert.False(harness.Coordinator.TryGetResult(Input(ordinary).SourceIdentity, out _));
            Assert.False(harness.Coordinator.TryGetResult(Input(outerPayload).SourceIdentity, out _));
            Assert.False(harness.Coordinator.TryGetResult(Input(innerPayload).SourceIdentity, out _));
            NestedAssertUnchanged(harness.Source, originals);
            DirectoryInfo output = harness.Output("unused").Directory!;
            if (output.Exists)
            {
                Assert.Empty(output.EnumerateFiles("*", SearchOption.AllDirectories));
            }

            SigningFile Input(FileInfo file) => parent is null
                ? SigningFile.Capture(file)
                : SigningFile.ContainerEntry(file, parent,
                    Path.GetRelativePath(harness.Source.FullName, file.FullName).Replace('\\', '/'));

            FileInfo Destination(FileInfo file) => outOfPlace
                ? harness.Output(Path.GetRelativePath(harness.Source.FullName, file.FullName))
                : file;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Nested_NoUpdateDoesNotResolveOrCompareApplicationContexts(bool reverse)
        {
            RecordingReader reader = new();
            await using Harness harness = new(reader: reader);
            FileInfo app = harness.Application(@"v1\App.manifest", "missing.dll");
            FileInfo first = harness.Deployment("Alpha.application", @"v1\App.manifest");
            FileInfo second = harness.Deployment("Beta.vsto", @"v1\App.manifest", mapped: true);
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            List<SigningOperationPlan> plans = new[]
            {
                first, app, second
            }.Select(file => new SigningOperationPlan(SigningFile.Capture(file),
                harness.Output(Path.GetRelativePath(harness.Source.FullName, file.FullName)))).ToList();
            if (reverse)
            {
                plans.Reverse();
            }

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 1,
                updateManifest: false).WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Empty(reader.ApplicationResolves);
            Assert.Empty(reader.DeploymentResolves);
            Assert.Equal(0, reader.ApplicationUpdates);
            Assert.Equal(0, reader.DeploymentUpdates);
            Assert.Equal(3, harness.Calls.Count);
            foreach (FileInfo source in new[] { first, app, second })
            {
                string relativePath = Path.GetRelativePath(harness.Source.FullName, source.FullName);
                Assert.Equal(System.IO.File.ReadAllText(source.FullName) + "\n<!--signed-->",
                    System.IO.File.ReadAllText(harness.Output(relativePath).FullName));
                Assert.Equal(1, harness.Calls.Count(name => name == source.Name));
            }
            NestedAssertFiles(harness.Output(first.Name).Directory!,
                new[] { first.Name, second.Name, @"v1\App.manifest" });
            NestedAssertUnchanged(harness.Source, originals);
        }

        private static async Task NestedVerifyApplicationContextAsync(bool explicitApplication, bool reverse,
            bool outOfPlace, int maxConcurrency, bool mapped, bool container, bool noSign = false)
        {
            await using Harness harness = new();
            ConcurrentQueue<string> payloadCalls = NestedObservePayloads(harness);
            string payloadPath = mapped ? @"v1\payload.dll.deploy" : "payload.dll";
            FileInfo payload = harness.File(payloadPath, "deployment selected payload");
            FileInfo? unsuffixed = mapped
                ? harness.File(@"v1\payload.dll", "unselected unsuffixed payload")
                : null;
            FileInfo app = harness.Application(@"v1\App.manifest", "payload.dll");
            FileInfo deployment = harness.Deployment("Root.application", @"v1\App.manifest", mapped);
            harness.File(@"v2\unselected.dll", "unselected version");
            SigningSourceIdentity? parent = container
                ? SigningSourceIdentity.Capture(harness.File("container.zip", "container identity control"))
                : null;
            SigningFile deploymentInput = parent is null
                ? SigningFile.Capture(deployment)
                : SigningFile.ContainerEntry(deployment, parent, "publish/Root.application");
            SigningFile applicationInput = parent is null
                ? SigningFile.Capture(app)
                : SigningFile.ContainerEntry(app, parent, "publish/v1/App.manifest");
            FileInfo output = outOfPlace ? harness.Output(deployment.Name) : deployment;
            FileInfo applicationOutput = outOfPlace ? harness.Output(@"v1\App.manifest") : app;
            List<SigningOperationPlan> plans = new()
            {
                new SigningOperationPlan(deploymentInput, output)
            };
            if (explicitApplication)
            {
                plans.Add(new SigningOperationPlan(applicationInput, applicationOutput));
            }
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            if (reverse)
            {
                plans.Reverse();
            }

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency,
                noSign ? ClickOnceApplicationSigningPolicy.UpdateManifestOnly : ClickOnceApplicationSigningPolicy.SignPayloads)
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(noSign ? 2 : 3, harness.Calls.Count);
            Assert.Equal(1, harness.Calls.Count(name => name == app.Name));
            Assert.Equal(1, harness.Calls.Count(name => name == deployment.Name));
            Assert.Equal(noSign ? Array.Empty<string>() : new[] { "deployment selected payload" }, payloadCalls.ToArray());
            FileInfo publishedPayload = new(Path.Combine(output.DirectoryName!, payloadPath));
            NestedAssertApplicationHash(applicationOutput, "payload.dll", noSign ? payload : publishedPayload);
            NestedAssertDeploymentHash(harness, output, applicationOutput);
            SigningSourceIdentity payloadIdentity = deploymentInput.GetSibling(payload, harness.Source).SourceIdentity;
            Assert.Equal(!noSign, harness.Coordinator.TryGetResult(payloadIdentity, out _));
            Assert.True(harness.Coordinator.TryGetResult(applicationInput.SourceIdentity,
                out Task<SigningOperationResult>? applicationResult));
            Assert.True(applicationResult!.IsCompletedSuccessfully);
            if (container)
            {
                Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(app).SourceIdentity, out _));
                Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(payload).SourceIdentity, out _));
            }
            if (unsuffixed is not null)
            {
                Assert.Equal("unselected unsuffixed payload", System.IO.File.ReadAllText(unsuffixed.FullName));
                Assert.False(harness.Coordinator.TryGetResult(
                    deploymentInput.GetSibling(unsuffixed, harness.Source).SourceIdentity, out _));
            }
            string[] selected = new[] { deployment.Name, @"v1\App.manifest" }
                .Concat(noSign ? Array.Empty<string>() : new[] { payloadPath }).ToArray();
            if (outOfPlace)
            {
                NestedAssertFiles(output.Directory!, selected);
                NestedAssertUnchanged(harness.Source, originals);
            }
            else
            {
                NestedAssertUnchanged(harness.Source, originals, selected);
            }
            if (!noSign)
            {
                Assert.Equal("deployment selected payload signed", System.IO.File.ReadAllText(publishedPayload.FullName));
            }
        }

        [Theory]
        [InlineData(false, false, false, 1)]
        [InlineData(false, true, true, 1)]
        [InlineData(false, false, true, 4)]
        [InlineData(false, true, false, 4)]
        [InlineData(true, false, true, 1)]
        [InlineData(true, true, true, 4)]
        public async Task Nested_ExplicitInnerApplicationUsesImplicitChildDeploymentContext(
            bool container, bool reverse, bool outOfPlace, int maxConcurrency)
        {
            await using Harness harness = new();
            ConcurrentQueue<string> payloadCalls = NestedObservePayloads(harness);
            FileInfo payload = harness.File("payload.dll", "implicit context payload");
            FileInfo inner = harness.Application(@"Other\Inner.manifest", payload.Name);
            FileInfo child = harness.Deployment("Child.application", @"Other\Inner.manifest");
            FileInfo outer = harness.Application(@"v1\Outer.manifest", child.Name);
            FileInfo root = harness.Deployment("Root.application", @"v1\Outer.manifest");
            harness.File(@"archive\Inner.manifest", "unselected archive");
            SigningSourceIdentity? parent = container
                ? SigningSourceIdentity.Capture(harness.File("container.zip", "container identity control"))
                : null;
            List<SigningOperationPlan> plans = new()
            {
                new SigningOperationPlan(Input(root), Destination(root)),
                new SigningOperationPlan(Input(inner), Destination(inner))
            };
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            if (reverse)
            {
                plans.Reverse();
            }

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency)
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(new[] { "implicit context payload" }, payloadCalls.ToArray());
            Assert.Equal(5, harness.Calls.Count);
            Assert.All(new[] { inner, child, outer, root },
                file => Assert.Equal(1, harness.Calls.Count(name => name == file.Name)));
            FileInfo publishedPayload = Destination(payload);
            Assert.Equal("implicit context payload signed", System.IO.File.ReadAllText(publishedPayload.FullName));
            NestedAssertApplicationHash(Destination(inner), payload.Name, publishedPayload);
            NestedAssertDeploymentHash(harness, Destination(child), Destination(inner));
            NestedAssertApplicationHash(Destination(outer), child.Name, Destination(child));
            NestedAssertDeploymentHash(harness, Destination(root), Destination(outer));
            foreach (FileInfo source in new[] { payload, inner, child, outer, root })
            {
                Assert.True(harness.Coordinator.TryGetResult(Input(source).SourceIdentity,
                    out Task<SigningOperationResult>? result));
                Assert.True(result!.IsCompletedSuccessfully);
                if (container)
                {
                    Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(source).SourceIdentity, out _));
                }
            }
            string[] selected = new[] { payload.Name, @"Other\Inner.manifest", child.Name,
                @"v1\Outer.manifest", root.Name };
            if (outOfPlace)
            {
                NestedAssertFiles(Destination(root).Directory!, selected);
                NestedAssertUnchanged(harness.Source, originals);
            }
            else
            {
                NestedAssertUnchanged(harness.Source, originals, selected);
            }

            SigningFile Input(FileInfo file) => parent is null
                ? SigningFile.Capture(file)
                : SigningFile.ContainerEntry(file, parent,
                    "publish/" + Path.GetRelativePath(harness.Source.FullName, file.FullName).Replace('\\', '/'));

            FileInfo Destination(FileInfo file) => outOfPlace
                ? harness.Output(Path.GetRelativePath(harness.Source.FullName, file.FullName))
                : file;
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Nested_DirectApplicationEntryPreservesImplicitDeploymentFallbackContext(
            bool container, bool outOfPlace)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File("payload.dll", "direct implicit payload");
            FileInfo inner = harness.Application(@"Other\Inner.manifest", payload.Name);
            FileInfo child = harness.Deployment("Child.vsto", @"Other\Inner.manifest");
            FileInfo outer = harness.Application("Outer.manifest", child.Name);
            SigningSourceIdentity? parent = container
                ? SigningSourceIdentity.Capture(harness.File("container.zip", "container identity control"))
                : null;
            SigningFile input = parent is null
                ? SigningFile.Capture(outer)
                : SigningFile.ContainerEntry(outer, parent, "publish/Outer.manifest");
            FileInfo output = outOfPlace ? harness.Output(outer.Name) : outer;
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);

            await harness.Pipeline.ExecuteApplicationAsync(new SigningOperationPlan(input, output),
                harness.Options, harness.Coordinator).WaitAsync(TimeSpan.FromSeconds(20));

            FileInfo publishedInner = new(Path.Combine(output.DirectoryName!, @"Other\Inner.manifest"));
            FileInfo publishedChild = new(Path.Combine(output.DirectoryName!, child.Name));
            FileInfo publishedPayload = new(Path.Combine(output.DirectoryName!, payload.Name));
            Assert.Equal("direct implicit payload signed", System.IO.File.ReadAllText(publishedPayload.FullName));
            NestedAssertApplicationHash(publishedInner, payload.Name, publishedPayload);
            NestedAssertDeploymentHash(harness, publishedChild, publishedInner);
            NestedAssertApplicationHash(output, child.Name, publishedChild);
            Assert.Equal(new[] { payload.Name, inner.Name, child.Name, outer.Name }, harness.Calls.ToArray());
            foreach (FileInfo source in new[] { payload, inner, child, outer })
            {
                SigningSourceIdentity identity = input.GetSibling(source, harness.Source).SourceIdentity;
                Assert.True(harness.Coordinator.TryGetResult(identity, out Task<SigningOperationResult>? result));
                Assert.True(result!.IsCompletedSuccessfully);
                if (container)
                {
                    Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(source).SourceIdentity, out _));
                }
            }
            string[] selected = new[] { outer.Name, child.Name, @"Other\Inner.manifest", payload.Name };
            if (outOfPlace)
            {
                NestedAssertFiles(output.Directory!, selected);
                NestedAssertUnchanged(harness.Source, originals);
            }
            else
            {
                NestedAssertUnchanged(harness.Source, originals, selected);
            }
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        public async Task Nested_ImplicitDeploymentDiamondRejectsConflictingFallbackContextsBeforeMutation(
            bool container, bool reverseReferences, bool outOfPlace)
        {
            await using Harness harness = new();
            harness.File(@"Root\payload.dll", "outer implicit fallback");
            harness.File(@"Root\Origin\payload.dll", "inner implicit fallback");
            FileInfo inner = harness.Application(@"Root\Origin\v1\Inner.manifest", "payload.dll");
            FileInfo first = harness.Deployment(@"Root\Origin\First.application", @"v1\Inner.manifest");
            FileInfo second = harness.Deployment(@"Root\Second.vsto", @"Origin\v1\Inner.manifest");
            string[] references = new[] { @"Origin\First.application", "Second.vsto" };
            if (reverseReferences)
            {
                Array.Reverse(references);
            }
            FileInfo outer = harness.Application(@"Root\Outer.manifest", references);
            FileInfo root = harness.Deployment(@"Root\Root.application", outer.Name);
            FileInfo ordinary = harness.File(@"Root\ordinary.dll", "independent ordinary payload");
            SigningSourceIdentity? parent = container
                ? SigningSourceIdentity.Capture(harness.File("container.zip", "container identity control"))
                : null;
            List<SigningOperationPlan> plans = new()
            {
                new SigningOperationPlan(Input(root), Destination(root)),
                new SigningOperationPlan(Input(ordinary), Destination(ordinary))
            };
            if (reverseReferences)
            {
                plans.Reverse();
            }
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);

            ClickOncePublishLayoutResolutionException failure =
                await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(() =>
                    harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 4)
                        .WaitAsync(TimeSpan.FromSeconds(20)));

            string paths = string.Join(", ", new[] { first.FullName, second.FullName }
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
            Assert.Equal($"The ClickOnce application manifest '{inner.FullName}' has incompatible source, " +
                $"destination, or file-mapping contexts in these deployment manifests: {paths}.", failure.Message);
            Assert.Empty(harness.Calls);
            Assert.False(harness.Coordinator.TryGetResult(Input(ordinary).SourceIdentity, out _));
            NestedAssertUnchanged(harness.Source, originals);
            DirectoryInfo output = harness.Output("unused").Directory!;
            if (output.Exists)
            {
                Assert.Empty(output.EnumerateFiles("*", SearchOption.AllDirectories));
            }

            SigningFile Input(FileInfo file) => parent is null
                ? SigningFile.Capture(file)
                : SigningFile.ContainerEntry(file, parent,
                    "publish/" + Path.GetRelativePath(harness.Source.FullName, file.FullName).Replace('\\', '/'));

            FileInfo Destination(FileInfo file) => outOfPlace
                ? harness.Output(Path.GetRelativePath(harness.Source.FullName, file.FullName))
                : file;
        }

        [Theory]
        [InlineData(false, false, false, 1)]
        [InlineData(false, true, false, 1)]
        [InlineData(false, false, true, 4)]
        [InlineData(false, true, true, 4)]
        [InlineData(true, false, true, 1)]
        [InlineData(true, true, true, 4)]
        public async Task Nested_ApplicationOnlyBatchDiscoversImplicitDeploymentContextBeforeStandaloneResolution(
            bool container, bool reverse, bool outOfPlace, int maxConcurrency)
        {
            await using Harness harness = new();
            ConcurrentQueue<string> payloadCalls = NestedObservePayloads(harness);
            FileInfo payload = harness.File("payload.dll", "application-only fallback payload");
            FileInfo inner = harness.Application(@"Other\Inner.manifest", payload.Name);
            FileInfo child = harness.Deployment("Child.application", @"Other\Inner.manifest");
            FileInfo outer = harness.Application("Outer.manifest", child.Name);
            SigningSourceIdentity? parent = container
                ? SigningSourceIdentity.Capture(harness.File("container.zip", "container identity control"))
                : null;
            List<SigningOperationPlan> plans = new()
            {
                new SigningOperationPlan(Input(inner), Destination(inner)),
                new SigningOperationPlan(Input(outer), Destination(outer))
            };
            if (reverse)
            {
                plans.Reverse();
            }
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency)
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(new[] { "application-only fallback payload" }, payloadCalls.ToArray());
            Assert.Equal(4, harness.Calls.Count);
            Assert.All(new[] { inner, child, outer },
                file => Assert.Equal(1, harness.Calls.Count(name => name == file.Name)));
            Assert.Equal("application-only fallback payload signed",
                System.IO.File.ReadAllText(Destination(payload).FullName));
            NestedAssertApplicationHash(Destination(inner), payload.Name, Destination(payload));
            NestedAssertDeploymentHash(harness, Destination(child), Destination(inner));
            NestedAssertApplicationHash(Destination(outer), child.Name, Destination(child));
            foreach (FileInfo source in new[] { payload, inner, child, outer })
            {
                Assert.True(harness.Coordinator.TryGetResult(Input(source).SourceIdentity,
                    out Task<SigningOperationResult>? result));
                Assert.True(result!.IsCompletedSuccessfully);
                if (container)
                {
                    Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(source).SourceIdentity, out _));
                }
            }
            Assert.False(System.IO.File.Exists(Path.Combine(Destination(inner).DirectoryName!, payload.Name)));
            string[] selected = new[] { outer.Name, child.Name, @"Other\Inner.manifest", payload.Name };
            if (outOfPlace)
            {
                NestedAssertFiles(Destination(outer).Directory!, selected);
                NestedAssertUnchanged(harness.Source, originals);
            }
            else
            {
                NestedAssertUnchanged(harness.Source, originals, selected);
            }

            SigningFile Input(FileInfo file) => parent is null
                ? SigningFile.Capture(file)
                : SigningFile.ContainerEntry(file, parent,
                    "publish/" + Path.GetRelativePath(harness.Source.FullName, file.FullName).Replace('\\', '/'));

            FileInfo Destination(FileInfo file) => outOfPlace
                ? harness.Output(Path.GetRelativePath(harness.Source.FullName, file.FullName))
                : file;
        }

        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 1)]
        [InlineData(false, 4)]
        [InlineData(true, 4)]
        public async Task Nested_UncoveredStandaloneApplicationFailureRetainsOriginalDiagnosticBeforeAnyMutation(
            bool reverse, int maxConcurrency)
        {
            await using Harness harness = new();
            FileInfo missing = harness.Application(@"Isolated\Missing.manifest", "missing.dll");
            harness.File("payload.dll", "valid root fallback");
            harness.Application(@"Other\Inner.manifest", "payload.dll");
            FileInfo child = harness.Deployment("Child.application", @"Other\Inner.manifest");
            FileInfo outer = harness.Application("Outer.manifest", child.Name);
            FileInfo ordinary = harness.File("ordinary.dll", "independently selected ordinary payload");
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            ClickOnceApplicationPublishLayoutResolver resolver = new(new ClickOnceManifestReader(),
                new ClickOncePayloadResolver());
            ClickOncePublishLayoutResolutionException original =
                Assert.Throws<ClickOncePublishLayoutResolutionException>(() =>
                {
                    resolver.TryResolve(missing, out ResolvedClickOncePublishLayout? unexpected);
                    unexpected?.Application.Manifest.Dispose();
                });
            List<SigningOperationPlan> plans = new[]
            {
                missing, outer, ordinary
            }.Select(file => new SigningOperationPlan(SigningFile.Capture(file),
                harness.Output(Path.GetRelativePath(harness.Source.FullName, file.FullName)))).ToList();
            if (reverse)
            {
                plans.Reverse();
            }

            ClickOncePublishLayoutResolutionException failure =
                await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(() =>
                    harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency)
                        .WaitAsync(TimeSpan.FromSeconds(20)));

            Assert.Equal(original.Message, failure.Message);
            Assert.Equal(original.GetType(), failure.GetType());
            Assert.Empty(harness.Calls);
            Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(ordinary).SourceIdentity, out _));
            NestedAssertUnchanged(harness.Source, originals);
            DirectoryInfo output = harness.Output("unused").Directory!;
            if (output.Exists)
            {
                Assert.Empty(output.EnumerateFiles("*", SearchOption.AllDirectories));
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task Nested_ApplicationOnlyMappedFallbackRetainsDistinctExplicitOutput(
            bool reverse, bool separateInnerOutput)
        {
            await using Harness harness = new();
            ConcurrentQueue<string> payloadCalls = NestedObservePayloads(harness);
            FileInfo mappedPayload = harness.File("payload.dll.deploy", "mapped application-only fallback");
            harness.File("payload.dll", "unselected unsuffixed fallback");
            FileInfo inner = harness.Application(@"Other\Inner.manifest", "payload.dll");
            FileInfo child = harness.Deployment("Child.application", @"Other\Inner.manifest", mapped: true);
            FileInfo outer = harness.Application("Outer.manifest", child.Name);
            FileInfo innerOutput = harness.Output(separateInnerOutput ? @"independent\Inner.manifest" : @"Other\Inner.manifest");
            List<SigningOperationPlan> plans = new()
            {
                new SigningOperationPlan(SigningFile.Capture(inner), innerOutput),
                new SigningOperationPlan(SigningFile.Capture(outer), harness.Output(outer.Name))
            };
            if (reverse)
            {
                plans.Reverse();
            }
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 1)
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(new[] { "mapped application-only fallback" }, payloadCalls.ToArray());
            Assert.Equal(new[] { "payload.dll", inner.Name, child.Name, outer.Name }, harness.Calls.ToArray());
            Assert.Equal("mapped application-only fallback signed",
                System.IO.File.ReadAllText(harness.Output(mappedPayload.Name).FullName));
            NestedAssertApplicationHash(harness.Output(@"Other\Inner.manifest"), "payload.dll",
                harness.Output(mappedPayload.Name));
            NestedAssertDeploymentHash(harness, harness.Output(child.Name), harness.Output(@"Other\Inner.manifest"));
            NestedAssertApplicationHash(harness.Output(outer.Name), child.Name, harness.Output(child.Name));
            List<string> selected = new() { outer.Name, child.Name, @"Other\Inner.manifest", mappedPayload.Name };
            if (separateInnerOutput)
            {
                FileInfo independentPayload = harness.Output(@"independent\payload.dll.deploy");
                Assert.Equal(System.IO.File.ReadAllBytes(harness.Output(@"Other\Inner.manifest").FullName),
                    System.IO.File.ReadAllBytes(innerOutput.FullName));
                Assert.Equal(System.IO.File.ReadAllBytes(harness.Output(mappedPayload.Name).FullName),
                    System.IO.File.ReadAllBytes(independentPayload.FullName));
                NestedAssertApplicationHash(innerOutput, "payload.dll", independentPayload);
                selected.Add(@"independent\Inner.manifest");
                selected.Add(@"independent\payload.dll.deploy");
            }
            Assert.False(System.IO.File.Exists(harness.Output(@"Other\payload.dll.deploy").FullName));
            Assert.False(System.IO.File.Exists(harness.Output("payload.dll").FullName));
            NestedAssertFiles(harness.Output(outer.Name).Directory!, selected);
            NestedAssertUnchanged(harness.Source, originals);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Nested_NonMissingReadAndTargetFailuresAreNotDeferredOrPublished(bool malformed, bool reverse)
        {
            await using Harness harness = new();
            FileInfo bad;
            Action resolveBad;
            if (malformed)
            {
                bad = harness.File("Broken.application", "<assembly>");
                ClickOnceDeploymentPublishLayoutResolver resolver = new(new ClickOnceManifestReader(),
                    new ClickOncePayloadResolver());
                resolveBad = () => resolver.Resolve(bad);
            }
            else
            {
                bad = harness.Application("Invalid.manifest", @"..\outside.dll");
                ClickOnceApplicationPublishLayoutResolver resolver = new(new ClickOnceManifestReader(),
                    new ClickOncePayloadResolver());
                resolveBad = () => resolver.TryResolve(bad, out _);
            }
            ClickOncePublishLayoutResolutionException expected =
                Assert.Throws<ClickOncePublishLayoutResolutionException>(resolveBad);
            harness.File("payload.dll", "valid graph fallback");
            harness.Application(@"Other\Inner.manifest", "payload.dll");
            FileInfo child = harness.Deployment("Child.application", @"Other\Inner.manifest");
            FileInfo outer = harness.Application("Outer.manifest", child.Name);
            FileInfo ordinary = harness.File("ordinary.dll", "independent ordinary payload");
            List<SigningOperationPlan> plans = new[]
            {
                bad, outer, ordinary
            }.Select(file => new SigningOperationPlan(SigningFile.Capture(file),
                harness.Output(file.Name))).ToList();
            if (reverse)
            {
                plans.Reverse();
            }
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);

            ClickOncePublishLayoutResolutionException failure =
                await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(() =>
                    harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 4)
                        .WaitAsync(TimeSpan.FromSeconds(20)));

            Assert.Equal(expected.Message, failure.Message);
            Assert.Empty(harness.Calls);
            Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(ordinary).SourceIdentity, out _));
            NestedAssertUnchanged(harness.Source, originals);
            DirectoryInfo output = harness.Output("unused").Directory!;
            if (output.Exists)
            {
                Assert.Empty(output.EnumerateFiles("*", SearchOption.AllDirectories));
            }
        }

        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 1)]
        [InlineData(false, 4)]
        [InlineData(true, 4)]
        public async Task Nested_AuthoritativeMappedContextDoesNotTraverseUnselectedStandaloneDeploymentGraph(
            bool reverse, int maxConcurrency)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File(@"Other\Good\payload.dll", "authoritative mapped graph payload");
            FileInfo good = harness.Application(@"Other\Good\Good.manifest", payload.Name);
            FileInfo unselected = harness.Deployment(@"Other\Child.application", "Missing.manifest");
            FileInfo mapped = harness.Deployment(@"Other\Child.application.deploy", @"Good\Good.manifest");
            FileInfo inner = harness.Application(@"Other\Inner.manifest", "Child.application");
            FileInfo bridge = harness.Deployment("B.application", @"Other\Inner.manifest", mapped: true);
            FileInfo outer = harness.Application("Outer.manifest", bridge.Name);
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            List<SigningOperationPlan> plans = new()
            {
                new SigningOperationPlan(SigningFile.Capture(inner), harness.Output(@"Other\Inner.manifest")),
                new SigningOperationPlan(SigningFile.Capture(outer), harness.Output(outer.Name))
            };
            if (reverse)
            {
                plans.Reverse();
            }

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency)
                .WaitAsync(TimeSpan.FromSeconds(20));

            FileInfo publishedPayload = harness.Output(@"Other\Good\payload.dll");
            FileInfo publishedGood = harness.Output(@"Other\Good\Good.manifest");
            FileInfo publishedChild = harness.Output(@"Other\Child.application.deploy");
            FileInfo publishedInner = harness.Output(@"Other\Inner.manifest");
            Assert.Equal("authoritative mapped graph payload signed",
                System.IO.File.ReadAllText(publishedPayload.FullName));
            NestedAssertApplicationHash(publishedGood, payload.Name, publishedPayload);
            NestedAssertDeploymentHash(harness, publishedChild, publishedGood);
            NestedAssertApplicationHash(publishedInner, "Child.application", publishedChild);
            NestedAssertDeploymentHash(harness, harness.Output(bridge.Name), publishedInner);
            NestedAssertApplicationHash(harness.Output(outer.Name), bridge.Name, harness.Output(bridge.Name));
            Assert.Equal(6, harness.Calls.Count);
            Assert.All(new[] { good, inner, bridge, outer },
                file => Assert.Equal(1, harness.Calls.Count(name => name == file.Name)));
            Assert.True(harness.Coordinator.TryGetResult(SigningFile.Capture(mapped).SourceIdentity,
                out Task<SigningOperationResult>? mappedResult));
            Assert.True(mappedResult!.IsCompletedSuccessfully);
            Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(unselected).SourceIdentity, out _));
            Assert.False(System.IO.File.Exists(harness.Output(@"Other\Child.application").FullName));
            NestedAssertFiles(harness.Output(outer.Name).Directory!, new[] { outer.Name, bridge.Name,
                @"Other\Inner.manifest", @"Other\Child.application.deploy", @"Other\Good\Good.manifest",
                @"Other\Good\payload.dll" });
            NestedAssertUnchanged(harness.Source, originals);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task Nested_WithoutAuthoritativeContextStandaloneSelectedChildFailureRemainsMandatory(bool reverse) =>
            NestedVerifySelectedChildFailureAsync(reverse, mappedAuthority: false);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task Nested_IndependentlyExplicitUnmappedChildRemainsMandatoryDespiteMappedAuthority(bool reverse) =>
            NestedVerifySelectedChildFailureAsync(reverse, mappedAuthority: true);

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Nested_PrunedStandaloneChildContextCannotConflictButExplicitChildContextMust(
            bool reverse, bool explicitUnmappedChild)
        {
            await using Harness harness = new();
            harness.File(@"Other\Good\payload.dll", "unmapped unselected payload");
            FileInfo mappedPayload = harness.File(@"Other\Good\payload.dll.deploy", "mapped live payload");
            FileInfo good = harness.Application(@"Other\Good\Good.manifest", "payload.dll");
            FileInfo unmappedChild = harness.Deployment(@"Other\Child.application", @"Good\Good.manifest");
            FileInfo mappedChild = harness.Deployment(@"Other\Child.application.deploy", @"Good\Good.manifest",
                mapped: true);
            FileInfo inner = harness.Application(@"Other\Inner.manifest", "Child.application");
            FileInfo bridge = harness.Deployment("B.application", @"Other\Inner.manifest", mapped: true);
            FileInfo outer = harness.Application("Outer.manifest", bridge.Name);
            List<FileInfo> selected = new() { inner, outer };
            if (explicitUnmappedChild)
            {
                selected.Add(unmappedChild);
            }
            List<SigningOperationPlan> plans = selected.Select(file =>
                new SigningOperationPlan(SigningFile.Capture(file),
                    harness.Output(Path.GetRelativePath(harness.Source.FullName, file.FullName)))).ToList();
            if (reverse)
            {
                plans.Reverse();
            }
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            Task operation = harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 4)
                .WaitAsync(TimeSpan.FromSeconds(20));
            if (explicitUnmappedChild)
            {
                ClickOncePublishLayoutResolutionException failure =
                    await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(() => operation);
                string paths = string.Join(", ", new[] { unmappedChild.FullName, mappedChild.FullName }
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
                Assert.Equal($"The ClickOnce application manifest '{good.FullName}' has incompatible source, " +
                    $"destination, or file-mapping contexts in these deployment manifests: {paths}.", failure.Message);
                Assert.Empty(harness.Calls);
                DirectoryInfo output = harness.Output("unused").Directory!;
                if (output.Exists)
                {
                    Assert.Empty(output.EnumerateFiles("*", SearchOption.AllDirectories));
                }
            }
            else
            {
                await operation;
                FileInfo publishedPayload = harness.Output(@"Other\Good\payload.dll.deploy");
                FileInfo publishedGood = harness.Output(@"Other\Good\Good.manifest");
                FileInfo publishedChild = harness.Output(@"Other\Child.application.deploy");
                FileInfo publishedInner = harness.Output(@"Other\Inner.manifest");
                Assert.Equal("mapped live payload signed", System.IO.File.ReadAllText(publishedPayload.FullName));
                NestedAssertApplicationHash(publishedGood, "payload.dll", publishedPayload);
                NestedAssertDeploymentHash(harness, publishedChild, publishedGood);
                NestedAssertApplicationHash(publishedInner, "Child.application", publishedChild);
                NestedAssertDeploymentHash(harness, harness.Output(bridge.Name), publishedInner);
                NestedAssertApplicationHash(harness.Output(outer.Name), bridge.Name, harness.Output(bridge.Name));
                Assert.Equal(6, harness.Calls.Count);
                Assert.True(harness.Coordinator.TryGetResult(SigningFile.Capture(mappedPayload).SourceIdentity, out _));
                Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(unmappedChild).SourceIdentity, out _));
                NestedAssertFiles(harness.Output(outer.Name).Directory!, new[] { outer.Name, bridge.Name,
                    @"Other\Inner.manifest", @"Other\Child.application.deploy", @"Other\Good\Good.manifest",
                    @"Other\Good\payload.dll.deploy" });
            }
            NestedAssertUnchanged(harness.Source, originals);
        }

        private static async Task NestedVerifySelectedChildFailureAsync(bool reverse, bool mappedAuthority)
        {
            await using Harness harness = new();
            harness.File(@"Other\Good\payload.dll", "mapped graph payload");
            harness.Application(@"Other\Good\Good.manifest", "payload.dll");
            FileInfo unselectedByAuthority = harness.Deployment(@"Other\Child.application", "Missing.manifest");
            harness.Deployment(@"Other\Child.application.deploy", @"Good\Good.manifest");
            FileInfo inner = harness.Application(@"Other\Inner.manifest", "Child.application");
            FileInfo outer;
            if (mappedAuthority)
            {
                FileInfo bridge = harness.Deployment("B.application", @"Other\Inner.manifest", mapped: true);
                outer = harness.Application("Outer.manifest", bridge.Name);
            }
            else
            {
                harness.File("ordinary-root.dll", "independent root payload");
                outer = harness.Application("OtherRoot.manifest", "ordinary-root.dll");
            }
            FileInfo ordinary = harness.File("ordinary.dll", "independently selected ordinary payload");
            ClickOnceDeploymentPublishLayoutResolver resolver = new(new ClickOnceManifestReader(),
                new ClickOncePayloadResolver());
            ClickOncePublishLayoutResolutionException expected =
                Assert.Throws<ClickOncePublishLayoutResolutionException>(() =>
                    resolver.Resolve(unselectedByAuthority));
            List<FileInfo> selected = new() { inner, outer, ordinary };
            if (mappedAuthority)
            {
                selected.Add(unselectedByAuthority);
            }
            List<SigningOperationPlan> plans = selected.Select(file =>
                new SigningOperationPlan(SigningFile.Capture(file),
                    harness.Output(Path.GetRelativePath(harness.Source.FullName, file.FullName)))).ToList();
            if (reverse)
            {
                plans.Reverse();
            }
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);

            ClickOncePublishLayoutResolutionException failure =
                await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(() =>
                    harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 4)
                        .WaitAsync(TimeSpan.FromSeconds(20)));

            Assert.Equal(expected.Message, failure.Message);
            Assert.Empty(harness.Calls);
            Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(ordinary).SourceIdentity, out _));
            NestedAssertUnchanged(harness.Source, originals);
            DirectoryInfo output = harness.Output("unused").Directory!;
            if (output.Exists)
            {
                Assert.Empty(output.EnumerateFiles("*", SearchOption.AllDirectories));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Nested_AuthoritativeMappedFallbackRetainsOriginalDeploymentRootAndContainerIdentity(bool container)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File(@"Good\payload.dll", "fallback authority payload");
            FileInfo good = harness.Application(@"Good\Good.manifest", payload.Name);
            harness.Deployment("Child.application", "Missing.manifest");
            FileInfo child = harness.Deployment("Child.application.deploy", @"Good\Good.manifest");
            FileInfo inner = harness.Application(@"Other\Inner.manifest", "Child.application");
            FileInfo bridge = harness.Deployment("B.application", @"Other\Inner.manifest", mapped: true);
            FileInfo outer = harness.Application("Outer.manifest", bridge.Name);
            SigningSourceIdentity? parent = container
                ? SigningSourceIdentity.Capture(harness.File("container.zip", "container identity control"))
                : null;
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            SigningOperationPlan[] plans = new[]
            {
                new SigningOperationPlan(Input(inner), harness.Output(@"Other\Inner.manifest")),
                new SigningOperationPlan(Input(outer), harness.Output(outer.Name))
            };

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 1)
                .WaitAsync(TimeSpan.FromSeconds(20));

            NestedAssertApplicationHash(harness.Output(@"Good\Good.manifest"), payload.Name,
                harness.Output(@"Good\payload.dll"));
            NestedAssertDeploymentHash(harness, harness.Output(child.Name), harness.Output(@"Good\Good.manifest"));
            NestedAssertApplicationHash(harness.Output(@"Other\Inner.manifest"), "Child.application",
                harness.Output(child.Name));
            NestedAssertDeploymentHash(harness, harness.Output(bridge.Name), harness.Output(@"Other\Inner.manifest"));
            NestedAssertApplicationHash(harness.Output(outer.Name), bridge.Name, harness.Output(bridge.Name));
            Assert.Equal(6, harness.Calls.Count);
            foreach (FileInfo source in new[] { payload, good, child, inner, bridge, outer })
            {
                Assert.True(harness.Coordinator.TryGetResult(Input(source).SourceIdentity, out _));
                if (container)
                {
                    Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(source).SourceIdentity, out _));
                }
            }
            NestedAssertFiles(harness.Output(outer.Name).Directory!, new[] { outer.Name, bridge.Name,
                @"Other\Inner.manifest", child.Name, @"Good\Good.manifest", @"Good\payload.dll" });
            NestedAssertUnchanged(harness.Source, originals);

            SigningFile Input(FileInfo file) => parent is null
                ? SigningFile.Capture(file)
                : SigningFile.ContainerEntry(file, parent,
                    "publish/" + Path.GetRelativePath(harness.Source.FullName, file.FullName).Replace('\\', '/'));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Nested_ConditionalMalformedOrUnsafeChildIsPrunedOnlyWithIndependentAuthority(
            bool unsafeTarget, bool mappedAuthority)
        {
            await using Harness harness = new();
            FileInfo bad = unsafeTarget
                ? harness.Deployment(@"Other\Child.application", @"..\outside.manifest")
                : harness.File(@"Other\Child.application", "<assembly>");
            ClickOnceDeploymentPublishLayoutResolver resolver = new(new ClickOnceManifestReader(),
                new ClickOncePayloadResolver());
            ClickOncePublishLayoutResolutionException expected =
                Assert.Throws<ClickOncePublishLayoutResolutionException>(() => resolver.Resolve(bad));
            harness.File(@"Other\Good\payload.dll", "conditional good payload");
            harness.Application(@"Other\Good\Good.manifest", "payload.dll");
            harness.Deployment(@"Other\Child.application.deploy", @"Good\Good.manifest");
            FileInfo inner = harness.Application(@"Other\Inner.manifest", "Child.application");
            FileInfo other;
            if (mappedAuthority)
            {
                FileInfo bridge = harness.Deployment("B.application", @"Other\Inner.manifest", mapped: true);
                other = harness.Application("Outer.manifest", bridge.Name);
            }
            else
            {
                other = harness.Application("OtherRoot.manifest");
            }
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            SigningOperationPlan[] plans = new[]
            {
                new SigningOperationPlan(SigningFile.Capture(inner), harness.Output(@"Other\Inner.manifest")),
                new SigningOperationPlan(SigningFile.Capture(other), harness.Output(other.Name))
            };
            Task operation = harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 1)
                .WaitAsync(TimeSpan.FromSeconds(20));
            if (mappedAuthority)
            {
                await operation;
                Assert.Equal(6, harness.Calls.Count);
                Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(bad).SourceIdentity, out _));
                NestedAssertDeploymentHash(harness, harness.Output(@"Other\Child.application.deploy"),
                    harness.Output(@"Other\Good\Good.manifest"));
                NestedAssertFiles(harness.Output(other.Name).Directory!, new[] { other.Name, "B.application",
                    @"Other\Inner.manifest", @"Other\Child.application.deploy", @"Other\Good\Good.manifest",
                    @"Other\Good\payload.dll" });
            }
            else
            {
                ClickOncePublishLayoutResolutionException failure =
                    await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(() => operation);
                Assert.Equal(expected.Message, failure.Message);
                Assert.Empty(harness.Calls);
                DirectoryInfo output = harness.Output("unused").Directory!;
                if (output.Exists)
                {
                    Assert.Empty(output.EnumerateFiles("*", SearchOption.AllDirectories));
                }
            }
            NestedAssertUnchanged(harness.Source, originals);
        }

        [Fact]
        public async Task Nested_IndependentlySupportedUnmappedChildRemainsMandatoryWithoutExplicitDeploymentSelection()
        {
            await using Harness harness = new();
            FileInfo bad = harness.Deployment(@"Other\Child.application", "Missing.manifest");
            harness.File(@"Other\Good\payload.dll", "mapped payload");
            harness.Application(@"Other\Good\Good.manifest", "payload.dll");
            harness.Deployment(@"Other\Child.application.deploy", @"Good\Good.manifest");
            FileInfo inner = harness.Application(@"Other\Inner.manifest", "Child.application");
            FileInfo bridge = harness.Deployment("B.application", @"Other\Inner.manifest", mapped: true);
            FileInfo outer = harness.Application("Outer.manifest", bridge.Name);
            FileInfo independent = harness.Application("Independent.manifest", @"Other\Child.application");
            ClickOnceDeploymentPublishLayoutResolver resolver = new(new ClickOnceManifestReader(),
                new ClickOncePayloadResolver());
            ClickOncePublishLayoutResolutionException expected =
                Assert.Throws<ClickOncePublishLayoutResolutionException>(() => resolver.Resolve(bad));
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            SigningOperationPlan[] plans = new[] { inner, outer, independent }
                .Select(file => new SigningOperationPlan(SigningFile.Capture(file),
                    harness.Output(Path.GetRelativePath(harness.Source.FullName, file.FullName)))).ToArray();

            ClickOncePublishLayoutResolutionException failure =
                await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(() =>
                    harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 4)
                        .WaitAsync(TimeSpan.FromSeconds(20)));

            Assert.Equal(expected.Message, failure.Message);
            Assert.Empty(harness.Calls);
            NestedAssertUnchanged(harness.Source, originals);
            DirectoryInfo output = harness.Output("unused").Directory!;
            if (output.Exists)
            {
                Assert.Empty(output.EnumerateFiles("*", SearchOption.AllDirectories));
            }
        }

        [Fact]
        public async Task Nested_UnsupportedStandaloneCycleIsPrunedByIndependentMappedAuthority()
        {
            await using Harness harness = new();
            harness.File(@"Other\Good\payload.dll", "cycle-free mapped payload");
            harness.Application(@"Other\Good\Good.manifest", "payload.dll");
            FileInfo cyclicChild = harness.Deployment(@"Other\Child.application", "Inner.manifest");
            harness.Deployment(@"Other\Child.application.deploy", @"Good\Good.manifest");
            FileInfo inner = harness.Application(@"Other\Inner.manifest", "Child.application");
            FileInfo bridge = harness.Deployment("B.application", @"Other\Inner.manifest", mapped: true);
            FileInfo outer = harness.Application("Outer.manifest", bridge.Name);
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            SigningOperationPlan[] plans = new[]
            {
                new SigningOperationPlan(SigningFile.Capture(inner), harness.Output(@"Other\Inner.manifest")),
                new SigningOperationPlan(SigningFile.Capture(outer), harness.Output(outer.Name))
            };

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 1)
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(6, harness.Calls.Count);
            Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(cyclicChild).SourceIdentity, out _));
            NestedAssertDeploymentHash(harness, harness.Output(@"Other\Child.application.deploy"),
                harness.Output(@"Other\Good\Good.manifest"));
            NestedAssertFiles(harness.Output(outer.Name).Directory!, new[] { outer.Name, bridge.Name,
                @"Other\Inner.manifest", @"Other\Child.application.deploy", @"Other\Good\Good.manifest",
                @"Other\Good\payload.dll" });
            NestedAssertUnchanged(harness.Source, originals);
        }

        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 1)]
        [InlineData(false, 4)]
        [InlineData(true, 4)]
        public async Task Nested_MutuallyInvalidatingApplicationAuthoritiesRejectBeforeAnyMutation(
            bool reverse, int maxConcurrency)
        {
            await using Harness harness = new();
            FileInfo leafA = harness.Application("LeafA.manifest");
            FileInfo leafB = harness.Application("LeafB.manifest");
            harness.Deployment("ToA.application.deploy", leafA.Name);
            harness.Deployment("ToB.application.deploy", leafB.Name);
            FileInfo appA = harness.Application("A.manifest", "ToB.application");
            FileInfo appB = harness.Application("B.manifest", "ToA.application");
            harness.Deployment("ToA.application", appA.Name, mapped: true);
            harness.Deployment("ToB.application", appB.Name, mapped: true);
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            List<SigningOperationPlan> plans = new[] { appA, appB }
                .Select(file => new SigningOperationPlan(SigningFile.Capture(file), harness.Output(file.Name)))
                .ToList();
            if (reverse)
            {
                plans.Reverse();
            }

            ClickOncePublishLayoutResolutionException failure =
                await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(() =>
                harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency)
                    .WaitAsync(TimeSpan.FromSeconds(20)));

            string paths = string.Join(", ", new[] { appA.FullName, appB.FullName }
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
            Assert.Equal("The ClickOnce application-manifest contexts are ambiguous because selecting one " +
                $"invalidates another's supporting deployment routes: {paths}.", failure.Message);
            Assert.Empty(harness.Calls);
            NestedAssertUnchanged(harness.Source, originals);
            DirectoryInfo output = harness.Output("unused").Directory!;
            if (output.Exists)
            {
                Assert.Empty(output.EnumerateFiles("*", SearchOption.AllDirectories));
            }
        }

        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 1)]
        [InlineData(false, 4)]
        [InlineData(true, 4)]
        public async Task Nested_ExplicitDeploymentAnchorResolvesOtherwiseMutualApplicationAuthorities(
            bool reverse, int maxConcurrency)
        {
            await using Harness harness = new();
            FileInfo leafA = harness.Application("LeafA.manifest");
            FileInfo leafB = harness.Application("LeafB.manifest");
            FileInfo mappedToA = harness.Deployment("ToA.application.deploy", leafA.Name);
            FileInfo unusedMappedToB = harness.Deployment("ToB.application.deploy", leafB.Name);
            FileInfo appA = harness.Application("A.manifest", "ToB.application");
            FileInfo appB = harness.Application("B.manifest", "ToA.application");
            FileInfo unusedToA = harness.Deployment("ToA.application", appA.Name, mapped: true);
            FileInfo anchoredToB = harness.Deployment("ToB.application", appB.Name, mapped: true);
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            List<SigningOperationPlan> plans = new[] { appA, appB, anchoredToB }
                .Select(file => new SigningOperationPlan(SigningFile.Capture(file), harness.Output(file.Name)))
                .ToList();
            if (reverse)
            {
                plans.Reverse();
            }

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency)
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(5, harness.Calls.Count);
            Assert.All(new[] { leafA, appA, appB, anchoredToB },
                file => Assert.Equal(1, harness.Calls.Count(name => name == file.Name)));
            Assert.True(harness.Coordinator.TryGetResult(SigningFile.Capture(mappedToA).SourceIdentity,
                out Task<SigningOperationResult>? result));
            Assert.True(result!.IsCompletedSuccessfully);
            Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(unusedToA).SourceIdentity, out _));
            Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(unusedMappedToB).SourceIdentity, out _));
            NestedAssertDeploymentHash(harness, harness.Output(mappedToA.Name), harness.Output(leafA.Name));
            NestedAssertApplicationHash(harness.Output(appB.Name), "ToA.application", harness.Output(mappedToA.Name));
            NestedAssertDeploymentHash(harness, harness.Output(anchoredToB.Name), harness.Output(appB.Name));
            NestedAssertApplicationHash(harness.Output(appA.Name), anchoredToB.Name, harness.Output(anchoredToB.Name));
            NestedAssertFiles(harness.Output(appA.Name).Directory!,
                new[] { appA.Name, appB.Name, anchoredToB.Name, mappedToA.Name, leafA.Name });
            NestedAssertUnchanged(harness.Source, originals);
        }

        [Theory]
        [InlineData(3, false, 1)]
        [InlineData(3, true, 1)]
        [InlineData(3, false, 4)]
        [InlineData(3, true, 4)]
        [InlineData(4, false, 1)]
        [InlineData(4, true, 1)]
        [InlineData(4, false, 4)]
        [InlineData(4, true, 4)]
        public Task Nested_LongerMutuallyInvalidatingAuthorityRingsRejectDeterministically(
            int length, bool reverse, int maxConcurrency) =>
            NestedVerifyAuthorityRingAsync(length, reverse, maxConcurrency, anchored: false);

        [Theory]
        [InlineData(3, false, 1)]
        [InlineData(3, true, 1)]
        [InlineData(3, false, 4)]
        [InlineData(3, true, 4)]
        [InlineData(4, false, 1)]
        [InlineData(4, true, 1)]
        [InlineData(4, false, 4)]
        [InlineData(4, true, 4)]
        public Task Nested_IndependentDeploymentAnchorBreaksLongerAuthorityRings(
            int length, bool reverse, int maxConcurrency) =>
            NestedVerifyAuthorityRingAsync(length, reverse, maxConcurrency, anchored: true);

        private static async Task NestedVerifyAuthorityRingAsync(int length, bool reverse, int maxConcurrency,
            bool anchored)
        {
            await using Harness harness = new();
            FileInfo[] leaves = new FileInfo[length];
            FileInfo[] applications = new FileInfo[length];
            FileInfo[] deployments = new FileInfo[length];
            FileInfo[] mappedDeployments = new FileInfo[length];
            for (int index = 0; index < length; ++index)
            {
                leaves[index] = harness.Application($"Leaf{index}.manifest");
                applications[index] = harness.Application($"App{index}.manifest",
                    $"To{(index + 1) % length}.application");
                deployments[index] = harness.Deployment($"To{index}.application", applications[index].Name,
                    mapped: true);
                mappedDeployments[index] = harness.Deployment($"To{index}.application.deploy", leaves[index].Name);
            }
            List<FileInfo> roots = applications.ToList();
            if (anchored)
            {
                roots.Add(deployments[1]);
            }
            List<SigningOperationPlan> plans = roots.Select(file =>
                new SigningOperationPlan(SigningFile.Capture(file), harness.Output(file.Name))).ToList();
            if (reverse)
            {
                plans.Reverse();
            }
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            Task operation = harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency)
                .WaitAsync(TimeSpan.FromSeconds(20));
            if (!anchored)
            {
                ClickOncePublishLayoutResolutionException failure =
                    await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(() => operation);
                string paths = string.Join(", ", applications.Select(file => file.FullName)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
                Assert.Equal("The ClickOnce application-manifest contexts are ambiguous because selecting one " +
                    $"invalidates another's supporting deployment routes: {paths}.", failure.Message);
                Assert.Empty(harness.Calls);
                DirectoryInfo output = harness.Output("unused").Directory!;
                if (output.Exists)
                {
                    Assert.Empty(output.EnumerateFiles("*", SearchOption.AllDirectories));
                }
            }
            else
            {
                await operation;
                bool[] mappedApplications = length == 3
                    ? new[] { true, true, false }
                    : new[] { false, true, false, true };
                HashSet<string> expectedFiles = new(StringComparer.OrdinalIgnoreCase);
                expectedFiles.Add(deployments[1].Name);
                NestedAssertDeploymentHash(harness, harness.Output(deployments[1].Name),
                    harness.Output(applications[1].Name));
                for (int index = 0; index < length; ++index)
                {
                    int next = (index + 1) % length;
                    FileInfo child = mappedApplications[index] ? mappedDeployments[next] : deployments[next];
                    FileInfo childApplication = mappedApplications[index] ? leaves[next] : applications[next];
                    expectedFiles.Add(applications[index].Name);
                    expectedFiles.Add(child.Name);
                    expectedFiles.Add(childApplication.Name);
                    NestedAssertApplicationHash(harness.Output(applications[index].Name),
                        deployments[next].Name, harness.Output(child.Name));
                    NestedAssertDeploymentHash(harness, harness.Output(child.Name),
                        harness.Output(childApplication.Name));
                    Assert.True(harness.Coordinator.TryGetResult(SigningFile.Capture(child).SourceIdentity,
                        out Task<SigningOperationResult>? result));
                    Assert.True(result!.IsCompletedSuccessfully);
                    FileInfo unused = mappedApplications[index] ? deployments[next] : mappedDeployments[next];
                    Assert.Equal(unused.FullName == deployments[1].FullName,
                        harness.Coordinator.TryGetResult(SigningFile.Capture(unused).SourceIdentity, out _));
                }
                Assert.Equal(expectedFiles.Count, harness.Calls.Count);
                Assert.All(applications,
                    file => Assert.Equal(1, harness.Calls.Count(name => name == file.Name)));
                NestedAssertFiles(harness.Output(applications[0].Name).Directory!, expectedFiles);
            }
            NestedAssertUnchanged(harness.Source, originals);
        }

        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 1)]
        [InlineData(false, 4)]
        [InlineData(true, 4)]
        public Task Nested_JointlyWithdrawnAlternativeAuthoritySupportsRejectBeforeMutation(
            bool reverse, int maxConcurrency) =>
            NestedVerifyJointAuthoritySupportsAsync(reverse, maxConcurrency, independentAnchor: false);

        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 1)]
        [InlineData(false, 4)]
        [InlineData(true, 4)]
        public Task Nested_IndependentApplicationAnchorPreservesJointAuthorityReplacement(
            bool reverse, int maxConcurrency) =>
            NestedVerifyJointAuthoritySupportsAsync(reverse, maxConcurrency, independentAnchor: true,
                anchorRemainingCycle: true);

        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 1)]
        [InlineData(false, 4)]
        [InlineData(true, 4)]
        public Task Nested_PartialIndependentAnchorStillRejectsRemainingJointAuthorityAmbiguity(
            bool reverse, int maxConcurrency) =>
            NestedVerifyJointAuthoritySupportsAsync(reverse, maxConcurrency, independentAnchor: true);

        private static async Task NestedVerifyJointAuthoritySupportsAsync(bool reverse, int maxConcurrency,
            bool independentAnchor, bool anchorRemainingCycle = false)
        {
            await using Harness harness = new();
            FileInfo leafA = harness.Application("LeafA.manifest");
            FileInfo leafB = harness.Application("LeafB.manifest");
            FileInfo leafC = harness.Application("LeafC.manifest");
            FileInfo mappedToA = harness.Deployment("ToA.application.deploy", leafA.Name);
            FileInfo mappedToB = harness.Deployment("ToB.application.deploy", leafB.Name);
            FileInfo mappedToC = harness.Deployment("ToC.application.deploy", leafC.Name);
            FileInfo appA = harness.Application("A.manifest", "ToB.application", "ToC.application");
            FileInfo appB = harness.Application("B.manifest", "ToA.application");
            FileInfo appC = harness.Application("C.manifest", "ToA.application");
            FileInfo toA = harness.Deployment("ToA.application", appA.Name, mapped: true);
            FileInfo toB = harness.Deployment("ToB.application", appB.Name, mapped: true);
            FileInfo toC = harness.Deployment("ToC.application", appC.Name, mapped: true);
            List<FileInfo> roots = new() { appA, appB, appC };
            FileInfo? anchor = independentAnchor
                ? harness.Application("D.manifest",
                    anchorRemainingCycle ? new[] { toA.Name, toB.Name } : new[] { toB.Name })
                : null;
            if (anchor is not null)
            {
                roots.Add(anchor);
            }
            List<SigningOperationPlan> plans = roots.Select(file =>
                new SigningOperationPlan(SigningFile.Capture(file), harness.Output(file.Name))).ToList();
            if (reverse)
            {
                plans.Reverse();
            }
            Dictionary<string, byte[]> originals = NestedSnapshot(harness.Source);
            Task operation = harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency)
                .WaitAsync(TimeSpan.FromSeconds(20));
            if (!independentAnchor || !anchorRemainingCycle)
            {
                ClickOncePublishLayoutResolutionException failure =
                    await Assert.ThrowsAsync<ClickOncePublishLayoutResolutionException>(() => operation);
                string[] ambiguousApplications = independentAnchor
                    ? new[] { appA.FullName, appC.FullName }
                    : new[] { appA.FullName, appB.FullName, appC.FullName };
                string paths = string.Join(", ", ambiguousApplications
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
                Assert.Equal("The ClickOnce application-manifest contexts are ambiguous because selecting one " +
                    $"invalidates another's supporting deployment routes: {paths}.", failure.Message);
                Assert.Empty(harness.Calls);
                DirectoryInfo output = harness.Output("unused").Directory!;
                if (output.Exists)
                {
                    Assert.Empty(output.EnumerateFiles("*", SearchOption.AllDirectories));
                }
            }
            else
            {
                await operation;
                FileInfo[] selected = new[] { appA, appB, appC, anchor!, leafA, leafB, leafC,
                    mappedToA, mappedToB, mappedToC, toA, toB };
                Assert.Equal(selected.Length, harness.Calls.Count);
                Assert.All(new[] { appA, appB, appC, anchor!, leafA, leafB, leafC },
                    file => Assert.Equal(1, harness.Calls.Count(name => name == file.Name)));
                NestedAssertApplicationHash(harness.Output(appA.Name), toB.Name, harness.Output(mappedToB.Name));
                NestedAssertApplicationHash(harness.Output(appA.Name), toC.Name, harness.Output(mappedToC.Name));
                NestedAssertApplicationHash(harness.Output(appB.Name), toA.Name, harness.Output(mappedToA.Name));
                NestedAssertApplicationHash(harness.Output(appC.Name), toA.Name, harness.Output(toA.Name));
                NestedAssertApplicationHash(harness.Output(anchor!.Name), toA.Name, harness.Output(toA.Name));
                NestedAssertApplicationHash(harness.Output(anchor!.Name), toB.Name, harness.Output(toB.Name));
                foreach ((FileInfo deployment, FileInfo application) in new[]
                {
                    (mappedToA, leafA), (mappedToB, leafB), (mappedToC, leafC), (toA, appA), (toB, appB)
                })
                {
                    NestedAssertDeploymentHash(harness, harness.Output(deployment.Name), harness.Output(application.Name));
                    Assert.True(harness.Coordinator.TryGetResult(SigningFile.Capture(deployment).SourceIdentity,
                        out Task<SigningOperationResult>? result));
                    Assert.True(result!.IsCompletedSuccessfully);
                }
                Assert.False(harness.Coordinator.TryGetResult(SigningFile.Capture(toC).SourceIdentity, out _));
                NestedAssertFiles(harness.Output(appA.Name).Directory!, selected.Select(file => file.Name));
            }
            NestedAssertUnchanged(harness.Source, originals);
        }

        private static ConcurrentQueue<string> NestedObservePayloads(Harness harness)
        {
            ConcurrentQueue<string> calls = new();
            harness.BeforeSign = file =>
            {
                if (file.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    calls.Enqueue(System.IO.File.ReadAllText(file.FullName));
                }
            };
            return calls;
        }

        private static Task NestedExecuteAsync(Harness harness, FileInfo input, FileInfo output,
            bool deploymentEntry, ClickOnceApplicationSigningPolicy policy = ClickOnceApplicationSigningPolicy.SignPayloads,
            bool updateManifest = true) =>
            deploymentEntry
                ? harness.ExecuteAsync(input, output, policy, updateManifest)
                : harness.Pipeline.ExecuteApplicationAsync(new SigningOperationPlan(SigningFile.Capture(input), output),
                    harness.Options, harness.Coordinator, policy, updateManifest);

        private static void NestedAssertDeploymentHash(Harness harness, FileInfo deployment, FileInfo application)
        {
            harness.AssertHash(deployment, application);
            ClickOnceManifestReader reader = new();
            using FileStream deploymentStream = deployment.OpenRead();
            using FileStream applicationStream = application.OpenRead();
            Assert.True(reader.TryReadDeployManifest(deploymentStream, out IDeployManifest? deploymentManifest));
            Assert.True(reader.TryReadApplicationManifest(applicationStream, out IApplicationManifest? applicationManifest));
            using (deploymentManifest)
            using (applicationManifest)
            {
                Assert.NotNull(deploymentManifest);
                Assert.NotNull(applicationManifest);
                AssemblyIdentity expected = applicationManifest.AssemblyIdentity;
                AssemblyIdentity actual = deploymentManifest.EntryPoint!.AssemblyIdentity;
                Assert.Equal(expected.Name, actual.Name);
                Assert.Equal(expected.Version, actual.Version);
                Assert.Equal(expected.ProcessorArchitecture, actual.ProcessorArchitecture);
                Assert.Equal(expected.Culture, actual.Culture);
                Assert.Equal(expected.PublicKeyToken, actual.PublicKeyToken);
            }
        }

        private static void NestedAssertApplicationHash(FileInfo application, string target, FileInfo payload,
            int expectedReferences = 1)
        {
            using FileStream stream = application.OpenRead();
            Assert.True(new ClickOnceManifestReader().TryReadApplicationManifest(stream, out IApplicationManifest? manifest));
            using (manifest)
            {
                Assert.NotNull(manifest);
                FileReference[] references = manifest.FileReferences.Cast<FileReference>()
                    .Where(file => file.TargetPath == target).ToArray();
                Assert.Equal(expectedReferences, references.Length);
                byte[] bytes = System.IO.File.ReadAllBytes(payload.FullName);
                Assert.All(references, reference =>
                {
                    Assert.Equal(bytes.Length, reference.Size);
                    Assert.Equal(Convert.ToBase64String(SHA256.HashData(bytes)), reference.Hash);
                });
            }
        }

        private static Dictionary<string, byte[]> NestedSnapshot(DirectoryInfo directory) =>
            directory.EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(file => !Path.GetRelativePath(directory.FullName, file.FullName)
                    .StartsWith("output" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(file => Path.GetRelativePath(directory.FullName, file.FullName),
                    file => System.IO.File.ReadAllBytes(file.FullName), StringComparer.OrdinalIgnoreCase);

        private static void NestedAssertUnchanged(DirectoryInfo directory, Dictionary<string, byte[]> originals,
            IEnumerable<string>? selectedInPlaceOutputs = null)
        {
            HashSet<string> selected = new(selectedInPlaceOutputs ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, byte[]> current = NestedSnapshot(directory);
            Assert.Equal(originals.Keys.Concat(selected).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                current.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
            foreach (KeyValuePair<string, byte[]> original in originals)
            {
                if (!selected.Contains(original.Key))
                {
                    Assert.Equal(original.Value, current[original.Key]);
                }
            }
        }

        private static void NestedAssertFiles(DirectoryInfo directory, IEnumerable<string> expected) =>
            Assert.Equal(expected.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                directory.EnumerateFiles("*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(directory.FullName, file.FullName))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

        private sealed partial class Harness
        {
            internal ClickOnceApplicationManifestSigner NestedApplicationPipeline(IClickOncePayloadSigningHandler handler) =>
                new(new ClickOnceApplicationPublishLayoutResolver(new ClickOnceManifestReader(), new ClickOncePayloadResolver()),
                    new ClickOncePublishLayoutStager(_services.GetRequiredService<IDirectoryService>()),
                    _services.GetRequiredService<IAggregatingDataFormatSigner>(),
                    _services.GetRequiredService<IFileMatcher>(),
                    _services.GetRequiredService<IManifestSigner>(),
                    _services.GetRequiredService<ICertificateProvider>(),
                    _services.GetRequiredService<ISignatureAlgorithmProvider>(),
                    Logger, handler);
        }
    }
}
