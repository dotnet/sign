// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.FileSystemGlobbing;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed partial class ClickOnceDeploymentManifestSignerTests
    {
        [Theory]
        [InlineData(1, false, false)]
        [InlineData(4, false, false)]
        [InlineData(1, true, false)]
        [InlineData(4, true, false)]
        [InlineData(1, true, true)]
        [InlineData(4, true, true)]
        public async Task Preflight_CrossGraphPublicationCollision_FailsBeforeMutation(
            int concurrency, bool caseAlias, bool existingDestination)
        {
            await using Harness harness = new();
            harness.File(@"first\v1\payload.dll", "first");
            harness.Application(@"first\v1\App.manifest", "payload.dll");
            FileInfo first = harness.Deployment(@"first\First.application", @"v1\App.manifest");
            string secondName = caseAlias ? "PAYLOAD.dll" : "payload.dll";
            harness.File(@"second\v1\" + secondName, "second");
            harness.Application(@"second\v1\Other.manifest", secondName);
            FileInfo second = harness.Deployment(@"second\Second.application", @"v1\Other.manifest");
            FileInfo sentinel = harness.Output(@"v1\payload.dll");
            if (existingDestination)
            {
                sentinel.Directory!.Create();
                System.IO.File.WriteAllText(sentinel.FullName, "sentinel");
            }
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(first), harness.Output(first.Name)),
                new(SigningFile.Capture(second), harness.Output(second.Name))
            };
            Dictionary<string, byte[]> before = Snapshot(harness);

            SigningException failure = await Assert.ThrowsAsync<SigningException>(() =>
                harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency));

            Assert.Contains(first.FullName, failure.Message);
            Assert.Contains(second.FullName, failure.Message);
            AssertUnchanged(harness, before);
        }

        [RequiresCaseSensitiveDirectoryFact]
        public async Task Preflight_CaseDistinctInPlaceInputs_RemainDistinct()
        {
            await using Harness harness = new();
            Assert.True(CaseSensitiveDirectory.TryEnable(harness.Source));
            FileInfo upper = harness.File("Payload.dll", "upper");
            FileInfo lower = harness.File("payload.dll", "lower");
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(upper), upper),
                new(SigningFile.Capture(lower), lower)
            };

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 4);

            Assert.Equal("upper signed", System.IO.File.ReadAllText(upper.FullName));
            Assert.Equal("lower signed", System.IO.File.ReadAllText(lower.FullName));
            Assert.Equal(2, harness.Calls.Count);
        }

        [RequiresCaseSensitiveDirectoryFact]
        public async Task Preflight_CaseDistinctOrdinaryAndGraphPublications_RemainDistinct()
        {
            await using Harness harness = new();
            FileInfo ordinary = harness.File("ordinary.dll", "ordinary");
            harness.File(@"v1\payload.dll", "payload");
            harness.Application(@"v1\App.manifest", "payload.dll");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest");
            FileInfo graphOutput = harness.Output(@"v1\payload.dll");
            graphOutput.Directory!.Create();
            Assert.True(CaseSensitiveDirectory.TryEnable(graphOutput.Directory));
            FileInfo ordinaryOutput = new(Path.Combine(graphOutput.DirectoryName!, "PAYLOAD.dll"));
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(ordinary), ordinaryOutput),
                new(SigningFile.Capture(deployment), harness.Output(deployment.Name))
            };

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 4);

            Assert.Equal("ordinary signed", System.IO.File.ReadAllText(ordinaryOutput.FullName));
            Assert.Equal("payload signed", System.IO.File.ReadAllText(graphOutput.FullName));
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(4, 0)]
        [InlineData(1, 1)]
        [InlineData(4, 1)]
        [InlineData(1, 2)]
        [InlineData(4, 2)]
        [InlineData(1, 3)]
        [InlineData(4, 3)]
        [InlineData(1, 4)]
        [InlineData(4, 4)]
        [InlineData(1, 5)]
        [InlineData(4, 5)]
        public async Task Preflight_OutputOverlapsExplicitOrGraphSource_FailsBeforeMutation(int concurrency, int target)
        {
            await using Harness harness = new();
            FileInfo ordinary = harness.File("ordinary.dll", "ordinary");
            FileInfo payload = harness.File(@"v1\payload.dll.deploy", "payload");
            FileInfo data = harness.File(@"v1\data.txt.deploy", "data");
            FileInfo application = harness.Application(@"v1\App.manifest", "payload.dll", "data.txt");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest", mapped: true);
            FileInfo launcher = harness.File("setup.exe", "setup");
            FileInfo explicitInput = harness.File("explicit.dll", "explicit");
            FileInfo conflicting = target switch
            {
                0 => deployment,
                1 => application,
                2 => payload,
                3 => data,
                4 => launcher,
                _ => explicitInput
            };
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(ordinary), conflicting),
                new(SigningFile.Capture(deployment), harness.Output(deployment.Name)),
                new(SigningFile.Capture(explicitInput), harness.Output(explicitInput.Name))
            };
            Dictionary<string, byte[]> before = Snapshot(harness);

            SigningException failure = await Assert.ThrowsAsync<SigningException>(() =>
                harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency));

            Assert.Equal(string.Format(CultureInfo.CurrentCulture, Resources.ClickOnceOwnershipCollision,
                conflicting.FullName, ordinary.FullName, conflicting.FullName, conflicting.FullName), failure.Message);
            Assert.Empty(harness.Calls);
            AssertUnchanged(harness, before);
        }

        [Theory]
        [InlineData(1, 0, false)]
        [InlineData(4, 0, false)]
        [InlineData(1, 1, false)]
        [InlineData(4, 1, false)]
        [InlineData(1, 2, false)]
        [InlineData(4, 2, false)]
        [InlineData(1, 3, false)]
        [InlineData(4, 3, false)]
        [InlineData(1, 4, false)]
        [InlineData(4, 4, false)]
        [InlineData(1, 5, false)]
        [InlineData(4, 5, false)]
        [InlineData(1, 2, true)]
        [InlineData(4, 2, true)]
        [InlineData(1, 4, true)]
        [InlineData(4, 4, true)]
        public async Task Preflight_ExplicitOutputConflictsWithGraphPublisher_FailsBeforeMutation(
            int concurrency, int target, bool mapped)
        {
            await using Harness harness = new();
            FileInfo ordinary = harness.File("ordinary.dll", "ordinary");
            string suffix = mapped ? ".deploy" : "";
            FileInfo payload = harness.File(@"v1\payload.dll" + suffix, "payload");
            FileInfo fallback = harness.File("fallback.dll" + suffix, "fallback");
            FileInfo nestedPayload = harness.File(@"v1\child\leaf.dll" + suffix, "leaf");
            harness.Application(@"v1\child\Child.manifest", "leaf.dll");
            FileInfo nested = harness.Deployment(@"v1\Nested.application" + suffix, @"child\Child.manifest", mapped);
            FileInfo application = harness.Application(@"v1\App.manifest", "payload.dll", "fallback.dll", "Nested.application");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest", mapped);
            FileInfo launcher = harness.File("setup.exe", "setup");
            FileInfo published = target switch
            {
                0 => deployment,
                1 => application,
                2 => payload,
                3 => fallback,
                4 => nestedPayload,
                _ => launcher
            };
            FileInfo destination = harness.Output(Path.GetRelativePath(harness.Source.FullName, published.FullName));
            harness.File(Path.GetRelativePath(harness.Source.FullName, destination.FullName), "output sentinel");
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(ordinary), destination),
                new(SigningFile.Capture(deployment), harness.Output(deployment.Name))
            };
            Dictionary<string, byte[]> before = Snapshot(harness);

            SigningException failure = await Assert.ThrowsAsync<SigningException>(() =>
                harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency));

            Assert.Equal(string.Format(CultureInfo.CurrentCulture, Resources.ClickOnceOwnershipCollision,
                destination.FullName, ordinary.FullName, deployment.FullName, published.FullName), failure.Message);
            AssertUnchanged(harness, before);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public async Task Preflight_SharedPayloadAndOrdinaryDuplicateOutputs_RemainSupported(int concurrency)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File(@"v1\payload.dll", "payload");
            harness.Application(@"v1\App.manifest", "payload.dll");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest");
            FileInfo first = harness.File("first.dll", "first");
            FileInfo second = harness.File("second.dll", "second");
            FileInfo ordinaryOutput = harness.Output("ordinary.dll");
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(payload), harness.Output(@"v1\payload.dll")),
                new(SigningFile.Capture(first), ordinaryOutput),
                new(SigningFile.Capture(second), ordinaryOutput),
                new(SigningFile.Capture(deployment), harness.Output(deployment.Name))
            };

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency);

            Assert.Equal("payload signed", System.IO.File.ReadAllText(harness.Output(@"v1\payload.dll").FullName));
            Assert.Equal("second signed", System.IO.File.ReadAllText(ordinaryOutput.FullName));
            Assert.Equal(5, harness.Calls.Count);
            Assert.Equal("payload", System.IO.File.ReadAllText(payload.FullName));
            harness.AssertHash(harness.Output(deployment.Name), harness.Output(@"v1\App.manifest"));
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(4, false)]
        [InlineData(1, true)]
        [InlineData(4, true)]
        public async Task Preflight_UnsignedCopyOverlapsStandaloneApplicationPayload_FailsBeforeMutation(
            int concurrency, bool noSignDependencies)
        {
            await using Harness harness = new();
            FileInfo ordinary = harness.File("ordinary.txt", "ordinary");
            FileInfo payload = harness.File("payload.dll", "payload");
            FileInfo application = harness.Application("App.manifest", payload.Name);
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(ordinary), payload),
                new(SigningFile.Capture(application), harness.Output(application.Name))
            };
            Dictionary<string, byte[]> before = Snapshot(harness);

            SigningException failure = await Assert.ThrowsAsync<SigningException>(() =>
                harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency,
                    noSignDependencies ? ClickOnceApplicationSigningPolicy.UpdateManifestOnly :
                        ClickOnceApplicationSigningPolicy.SignPayloads));

            Assert.Contains(ordinary.FullName, failure.Message);
            Assert.Contains(payload.FullName, failure.Message);
            Assert.Empty(harness.Calls);
            AssertUnchanged(harness, before);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(4, false)]
        [InlineData(1, true)]
        [InlineData(4, true)]
        public async Task ExecuteAsync_WhenNoUpdateOutputConflictsWithExplicitSourceOrManifestDestination_FailsWithoutMutation(
            int concurrency, bool sourceCollision)
        {
            await using Harness harness = new();
            FileInfo ordinary = harness.File("ordinary.dll", "ordinary");
            FileInfo deployment = harness.Deployment("App.application", "missing.manifest");
            FileInfo output = harness.Output(deployment.Name);
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(ordinary), sourceCollision ? deployment : output),
                new(SigningFile.Capture(deployment), output)
            };
            Dictionary<string, byte[]> before = Snapshot(harness);

            SigningException failure = await Assert.ThrowsAsync<SigningException>(() =>
                harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency,
                    updateManifest: false));

            Assert.Contains(ordinary.FullName, failure.Message);
            Assert.Contains(sourceCollision ? deployment.FullName : output.FullName, failure.Message);
            AssertUnchanged(harness, before);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(4, false)]
        [InlineData(1, true)]
        [InlineData(4, true)]
        public async Task ExecuteAsync_WhenExplicitOutputConflictsWithStandaloneApplicationPublication_FailsWithoutMutation(
            int concurrency, bool unsigned)
        {
            await using Harness harness = new();
            FileInfo ordinary = harness.File(unsigned ? "ordinary.txt" : "ordinary.dll", "ordinary");
            FileInfo payload = harness.File("payload.dll", "payload");
            FileInfo application = harness.Application("App.manifest", payload.Name);
            FileInfo destination = harness.Output(payload.Name);
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(ordinary), destination),
                new(SigningFile.Capture(application), harness.Output(application.Name))
            };
            Dictionary<string, byte[]> before = Snapshot(harness);

            SigningException failure = await Assert.ThrowsAsync<SigningException>(() =>
                harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency));

            Assert.Equal(string.Format(CultureInfo.CurrentCulture, Resources.ClickOnceOwnershipCollision,
                destination.FullName, ordinary.FullName, application.FullName, payload.FullName), failure.Message);
            AssertUnchanged(harness, before);
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(4, 0)]
        [InlineData(1, 1)]
        [InlineData(4, 1)]
        [InlineData(1, 2)]
        [InlineData(4, 2)]
        public async Task Preflight_UnpublishedPayloadDestination_RemainsAvailable(int concurrency, int mode)
        {
            await using Harness harness = new();
            FileInfo ordinary = harness.File("ordinary.dll", "ordinary");
            FileInfo payload = harness.File(@"v1\payload.dll", "payload");
            FileInfo application = harness.Application(@"v1\App.manifest", "payload.dll");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest");
            if (mode == 0)
            {
                Matcher excluded = new();
                excluded.AddInclude("**/payload.dll");
                harness.Options = new(null, null, null, null, HashAlgorithmName.SHA256, HashAlgorithmName.SHA256,
                    new Uri("https://timestamp.test"), null, excluded, true);
            }
            FileInfo destination = harness.Output(@"v1\payload.dll");
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(ordinary), destination),
                new(SigningFile.Capture(deployment), harness.Output(deployment.Name))
            };

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency,
                mode == 1 ? ClickOnceApplicationSigningPolicy.UpdateManifestOnly :
                    ClickOnceApplicationSigningPolicy.SignPayloads,
                updateManifest: mode != 2);

            Assert.Equal("ordinary signed", System.IO.File.ReadAllText(destination.FullName));
            Assert.Equal("payload", System.IO.File.ReadAllText(payload.FullName));
            Assert.DoesNotContain("payload.dll", harness.Calls);
            Assert.Equal(mode == 0, System.IO.File.Exists(harness.Output(@"v1\App.manifest").FullName));
            if (mode == 0)
            {
                harness.AssertHash(harness.Output(deployment.Name), harness.Output(@"v1\App.manifest"));
            }
            else if (mode == 1)
            {
                harness.AssertHash(harness.Output(deployment.Name), application);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task Preflight_RenamedPrimaryConflictsWithOwnGraphPublication_FailsBeforeMutation(
            bool applicationOnly, bool batch)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File("payload.dll", "payload");
            FileInfo application = harness.Application("App.manifest", payload.Name);
            FileInfo manifest = applicationOnly ? application : harness.Deployment("App.application", application.Name);
            SigningOperationPlan plan = new(SigningFile.Capture(manifest), harness.Output(payload.Name));
            Dictionary<string, byte[]> before = Snapshot(harness);

            SigningException failure = await Assert.ThrowsAsync<SigningException>(() => batch
                ? harness.Pipeline.ExecuteAsync(new[] { plan }, harness.Options, harness.Coordinator, 4)
                : applicationOnly
                    ? harness.Pipeline.ExecuteApplicationAsync(plan, harness.Options, harness.Coordinator)
                    : harness.Pipeline.ExecuteAsync(plan, harness.Options, harness.Coordinator));

            Assert.Equal(string.Format(CultureInfo.CurrentCulture, Resources.ClickOnceOwnershipCollision,
                plan.Output.FullName, manifest.FullName, manifest.FullName, payload.FullName), failure.Message);
            AssertUnchanged(harness, before);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(4, false)]
        [InlineData(1, true)]
        [InlineData(4, true)]
        public async Task Preflight_UnsignedMappedExplicitInput_AllowsOnlyInPlaceNoOp(int concurrency, bool inPlace)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File(@"v1\payload.dll.deploy", "payload");
            harness.Application(@"v1\App.manifest", "payload.dll");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest", mapped: true);
            FileInfo payloadOutput = inPlace ? payload : harness.Output(@"v1\payload.dll.deploy");
            FileInfo deploymentOutput = inPlace ? deployment : harness.Output(deployment.Name);
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(payload), payloadOutput),
                new(SigningFile.Capture(deployment), deploymentOutput)
            };
            Dictionary<string, byte[]> before = Snapshot(harness);

            if (inPlace)
            {
                await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency);

                Assert.Equal("payload signed", System.IO.File.ReadAllText(payload.FullName));
                Assert.Equal(1, harness.Calls.Count(name => name == "payload.dll"));
                harness.AssertHash(deployment, new FileInfo(Path.Combine(harness.Source.FullName, @"v1\App.manifest")));
            }
            else
            {
                SigningException failure = await Assert.ThrowsAsync<SigningException>(() =>
                    harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency));

                Assert.Equal(string.Format(CultureInfo.CurrentCulture, Resources.ClickOnceOwnershipCollision,
                    payloadOutput.FullName, payload.FullName, deployment.FullName, payload.FullName), failure.Message);
                AssertUnchanged(harness, before);
            }
        }

        private static Dictionary<string, byte[]> Snapshot(Harness harness) =>
            harness.Source.EnumerateFiles("*", SearchOption.AllDirectories)
                .ToDictionary(file => file.FullName, file => System.IO.File.ReadAllBytes(file.FullName),
                    StringComparer.Ordinal);

        private static void AssertUnchanged(Harness harness, Dictionary<string, byte[]> before)
        {
            Dictionary<string, byte[]> after = Snapshot(harness);
            Assert.Equal(before.Keys.OrderBy(path => path, StringComparer.Ordinal),
                after.Keys.OrderBy(path => path, StringComparer.Ordinal));
            foreach ((string path, byte[] bytes) in before)
            {
                Assert.Equal(bytes, after[path]);
            }
        }
    }
}
