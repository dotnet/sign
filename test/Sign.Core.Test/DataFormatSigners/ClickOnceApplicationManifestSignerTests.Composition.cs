// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.FileSystemGlobbing;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed partial class ClickOnceApplicationManifestSignerTests
    {
        [Theory]
        [InlineData("Child.application", "Child.application", false)]
        [InlineData("Child.application", "Child.application.deploy", false)]
        [InlineData("Child.deploy", "Child.deploy", false)]
        [InlineData("Child.deploy", "Child.deploy.deploy", false)]
        [InlineData("Child.application", "Child.application", true)]
        [InlineData("Child.application", "Child.application.deploy", true)]
        public async Task ExecuteAsync_WhenCompositeHandlerSignsPayload_PreservesOriginalSourceAndUpdateDestination(
            string target, string physical, bool container)
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile(physical, "unsigned");
            FileInfo app = harness.CreateManifest("App.manifest", target);
            SigningFile input = container
                ? SigningFile.ContainerEntry(app,
                    SigningSourceIdentity.ContainerEntry(
                        SigningSourceIdentity.Capture(harness.CreateFile("outer.zip")), "inner.zip"),
                    "app/App.manifest")
                : SigningFile.Capture(app);
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            ClickOncePayloadSigningRequest? selected = null;
            handler.CanSign(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>())
                .Returns(call =>
                {
                    selected = call.Arg<ClickOncePayloadSigningRequest>();
                    return true;
                });
            handler.SignAsync(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>(),
                Arg.Any<SigningOperationCoordinator>()).Returns(async call =>
                {
                    ClickOncePayloadSigningRequest request = call.Arg<ClickOncePayloadSigningRequest>();
                    Assert.Same(selected, request);
                    Assert.Equal(payload.FullName, request.Source.File.FullName);
                    Assert.Equal(input.GetSibling(payload, harness.Source).SourceIdentity, request.Source.SourceIdentity);
                    Assert.Equal(harness.Source.FullName, request.SourceRoot.FullName);
                    Assert.NotEqual(request.SourceRoot.FullName, request.StagingRoot.FullName);
                    Assert.Equal(target, request.UpdateFile.Name);
                    Assert.Equal(physical, request.StagedFile.Destination.Name);
                    Assert.Equal(physical != target, request.StagedFile.FileInfoUpdateFile is not null);
                    Assert.Equal("unsigned", File.ReadAllText(request.UpdateFile.FullName));
                    FileInfo signed = harness.CreateFile("signed.bin", "composed");
                    SigningOperationResult result = await harness.Coordinator.ExecuteAsync(
                        request.Source.SourceIdentity, () => Task.FromResult(signed));
                    return new ClickOncePayloadSigningResult(result);
                });
            harness.Pipeline = harness.CreatePipeline(harness.Reader, payloadHandler: handler);

            await harness.Pipeline.ExecuteAsync(new SigningOperationPlan(input, app),
                harness.Options(), harness.Coordinator, ClickOnceApplicationSigningPolicy.SignPayloads);

            Assert.Equal("composed", File.ReadAllText(payload.FullName));
            Assert.Empty(harness.PayloadCalls);
            Assert.Single(harness.ManifestCalls);
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public async Task SignOwnerAsync_WhenResolvedLayoutAndOriginAreProvided_UsesAuthoritativeOriginWithoutRediscovery()
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile("payload.dll.deploy");
            FileInfo app = harness.CreateManifest(@"version\App.manifest", "payload.dll");
            FileInfo deployment = harness.CreateFile("Root.application");
            IApplicationManifest manifest;
            using (FileStream stream = app.OpenRead())
            {
                Assert.True(harness.Reader.TryReadApplicationManifest(stream, out IApplicationManifest? read));
                manifest = read!;
            }
            ResolvedClickOncePayload resolved = new(payload, Assert.Single(manifest.FileReferences.Cast<FileReference>()));
            ResolvedClickOncePublishLayout layout = new(null,
                new ResolvedClickOnceApplication(app, manifest, new[] { resolved }),
                Array.Empty<ResolvedClickOnceAdjacentExecutable>(), Array.Empty<ClickOnceManifestDiagnostic>());
            IClickOnceManifestReader unusedReader = Substitute.For<IClickOnceManifestReader>();
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            handler.CanSign(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>()).Returns(true);
            handler.SignAsync(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>(),
                Arg.Any<SigningOperationCoordinator>()).Returns(async call =>
                {
                    ClickOncePayloadSigningRequest request = call.Arg<ClickOncePayloadSigningRequest>();
                    Assert.Equal(payload.FullName, request.Source.File.FullName);
                    Assert.Equal(harness.Source.FullName, request.SourceRoot.FullName);
                    Assert.Equal(SigningFile.Capture(payload).SourceIdentity, request.Source.SourceIdentity);
                    SigningOperationResult result = await harness.Coordinator.ExecuteAsync(
                        request.Source.SourceIdentity, () => Task.FromResult(harness.CreateFile("signed.bin", "signed")));
                    return new ClickOncePayloadSigningResult(result);
                });
            harness.Pipeline = harness.CreatePipeline(unusedReader, payloadHandler: handler);

            using SigningOperationArtifact artifact = await harness.Pipeline.SignOwnerAsync(
                SigningFile.Capture(app), harness.Options(), harness.Coordinator,
                ClickOnceApplicationSigningPolicy.SignPayloads, layout, SigningFile.Capture(deployment));

            Assert.Empty(unusedReader.ReceivedCalls());
            Assert.Equal("signed", File.ReadAllText(Path.Combine(artifact.RootDirectory.FullName, "payload.dll.deploy")));
        }

        [Fact]
        public async Task ExecuteAsync_WhenComposedPayloadCompletes_MaterializesBeforeMetadataAndManifestSigning()
        {
            await using Harness harness = new();
            harness.CreateFile("payload.dll");
            FileInfo app = harness.CreateManifest("App.manifest", "payload.dll");
            IApplicationManifest manifest = harness.UseFakeManifest("App.manifest", "payload.dll");
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            List<string> order = new();
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            handler.CanSign(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>()).Returns(true);
            handler.SignAsync(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>(),
                Arg.Any<SigningOperationCoordinator>()).Returns(async call =>
                {
                    ClickOncePayloadSigningRequest request = call.Arg<ClickOncePayloadSigningRequest>();
                    entered.SetResult();
                    await release.Task;
                    SigningOperationResult result = await harness.Coordinator.ExecuteAsync(
                        request.Source.SourceIdentity, () => Task.FromResult(harness.CreateFile("signed.bin", "signed")));
                    order.Add("result");
                    return new ClickOncePayloadSigningResult(result);
                });
            manifest.When(value => value.SynchronizeAssemblyIdentities()).Do(_ =>
            {
                Assert.Equal("signed", File.ReadAllText(Assert.Single(harness.FakeReferences).ResolvedPath!));
                order.Add("identity");
            });
            harness.BeforeUpdate = () => order.Add("update");
            harness.BeforeManifestSign = _ => order.Add("sign");
            harness.Pipeline = harness.CreatePipeline(harness.Reader, payloadHandler: handler);
            Task operation = harness.ExecuteAsync(app);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                Assert.Empty(order);
                Assert.Empty(harness.ManifestCalls);
            }
            finally
            {
                release.SetResult();
            }
            await operation.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(new[] { "result", "identity", "update", "sign" }, order);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_WhenChildResultIncludesExplicitCompanions_PublishesOnlyPrimaryAndExplicitOutputs(bool duplicateOutput)
        {
            await using Harness harness = new();
            FileInfo child = harness.CreateFile("Child.application", "unsigned");
            FileInfo app = harness.CreateManifest("App.manifest", "Child.application");
            FileInfo output = new(Path.Combine(harness.Source.FullName, "out", app.Name));
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            handler.CanSign(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>()).Returns(true);
            handler.SignAsync(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>(),
                Arg.Any<SigningOperationCoordinator>()).Returns(async call =>
                {
                    ClickOncePayloadSigningRequest request = call.Arg<ClickOncePayloadSigningRequest>();
                    FileInfo signedChild = harness.CreateFile(@"signed\Child.application", "signed child");
                    harness.CreateFile(@"signed\unselected.txt", "must not copy");
                    SigningOperationResult primary = await harness.Coordinator.ExecuteArtifactAsync(
                        request.Source.SourceIdentity,
                        () => Task.FromResult(SigningOperationArtifact.Layout(signedChild, signedChild.Directory!)));
                    FileInfo companion = harness.CreateFile(@"original\Other.manifest", "signed companion");
                    SigningOperationResult companionResult = await harness.Coordinator.ExecuteAsync(
                        SigningSourceIdentity.Capture(companion), () => Task.FromResult(companion));
                    FileInfo companionOutput = new(Path.Combine(request.StagingRoot.FullName, "nested", "Other.manifest"));
                    List<ClickOncePayloadSigningOutput> outputs = new()
                    {
                        new ClickOncePayloadSigningOutput(
                            new SigningOperationPlan(SigningFile.Capture(companion), companionOutput),
                            companionResult)
                    };
                    if (duplicateOutput)
                    {
                        outputs.Add(new ClickOncePayloadSigningOutput(
                            new SigningOperationPlan(SigningFile.Capture(companion),
                                new FileInfo(companionOutput.FullName.ToUpperInvariant())), companionResult));
                    }
                    return new ClickOncePayloadSigningResult(primary, outputs);
                });
            harness.Pipeline = harness.CreatePipeline(harness.Reader, payloadHandler: handler);

            await harness.ExecuteAsync(app, output);

            Assert.Equal("signed child", File.ReadAllText(Path.Combine(output.DirectoryName!, child.Name)));
            Assert.Equal("signed companion", File.ReadAllText(
                Path.Combine(output.DirectoryName!, "nested", "Other.manifest")));
            Assert.Equal("unsigned", File.ReadAllText(child.FullName));
            Assert.False(File.Exists(Path.Combine(output.DirectoryName!, "unselected.txt")));
            Assert.Equal(3, output.Directory!.EnumerateFiles("*", SearchOption.AllDirectories).Count());
            using FileStream stream = output.OpenRead();
            Assert.True(harness.Reader.TryReadApplicationManifest(stream, out IApplicationManifest? updated));
            using (updated)
            {
                Assert.Equal(Convert.ToBase64String(SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes("signed child"))), Assert.Single(updated!.FileReferences.Cast<FileReference>()).Hash);
            }
        }

        [Theory]
        [InlineData("App.manifest")]
        [InlineData("Child.application")]
        [InlineData("Child.application.deploy")]
        [InlineData(@"Child.application\nested.bin")]
        [InlineData(@"..\outside.bin")]
        public async Task ExecuteAsync_WhenChildOutputIsUnsafeOrReserved_RejectsBeforeMaterialization(string target)
        {
            await using Harness harness = new();
            FileInfo child = harness.CreateFile("Child.application.deploy", "unsigned");
            FileInfo app = harness.CreateManifest("App.manifest", "Child.application");
            string original = File.ReadAllText(app.FullName);
            string? stagedPrimary = null;
            string? extraDestination = null;
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            handler.CanSign(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>()).Returns(true);
            handler.SignAsync(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>(),
                Arg.Any<SigningOperationCoordinator>()).Returns(async call =>
                {
                    ClickOncePayloadSigningRequest request = call.Arg<ClickOncePayloadSigningRequest>();
                    stagedPrimary = request.UpdateFile.FullName;
                    FileInfo signed = harness.CreateFile("signed.bin", "signed");
                    SigningOperationResult result = await harness.Coordinator.ExecuteAsync(
                        request.Source.SourceIdentity, () => Task.FromResult(signed));
                    FileInfo extra = new(Path.Combine(request.StagingRoot.FullName, target));
                    extraDestination = extra.FullName;
                    return new ClickOncePayloadSigningResult(result, new[]
                    {
                        new ClickOncePayloadSigningOutput(new SigningOperationPlan(SigningFile.Capture(signed), extra), result)
                    });
                });
            harness.Pipeline = harness.CreatePipeline(harness.Reader, payloadHandler: handler);

            await Assert.ThrowsAsync<ClickOnceStagingException>(() => harness.ExecuteAsync(app));

            Assert.Equal(original, File.ReadAllText(app.FullName));
            Assert.Equal("unsigned", File.ReadAllText(child.FullName));
            Assert.Empty(harness.ManifestCalls);
            Assert.NotNull(stagedPrimary);
            Assert.False(File.Exists(stagedPrimary));
            if (target.Contains("..", StringComparison.Ordinal))
            {
                Assert.False(File.Exists(extraDestination));
            }
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public async Task ExecuteAsync_WhenHandlerDeclinesOrdinaryFormats_PreservesBatchSigningAndMetadata()
        {
            await using Harness harness = new();
            harness.CreateFile("first.dll");
            harness.CreateFile("second.dll");
            FileInfo excluded = harness.CreateFile("excluded.dll");
            FileInfo unsupported = harness.CreateFile("data.txt");
            FileInfo app = harness.CreateManifest("App.manifest",
                "first.dll", "second.dll", "excluded.dll", "data.txt");
            Matcher antiMatcher = new();
            antiMatcher.AddInclude("excluded.dll");
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            harness.Pipeline = harness.CreatePipeline(harness.Reader, payloadHandler: handler);

            await harness.ExecuteAsync(app, options: harness.Options(antiMatcher: antiMatcher));

            Assert.Equal(2, harness.PayloadCalls.Count);
            await harness.FormatSigner.Received(1).SignAsync(Arg.Is<IEnumerable<FileInfo>>(files => files.Count() == 2),
                Arg.Any<SignOptions>());
            Assert.DoesNotContain(handler.ReceivedCalls(), call => call.GetMethodInfo().Name == "SignAsync");
            Assert.Equal(3, handler.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "CanSign"));
            Assert.Equal("payload", File.ReadAllText(excluded.FullName));
            Assert.Equal("payload", File.ReadAllText(unsupported.FullName));
            using FileStream stream = app.OpenRead();
            Assert.True(harness.Reader.TryReadApplicationManifest(stream, out IApplicationManifest? updated));
            using (updated)
            {
                Assert.Equal(4, updated!.FileReferences.Count);
                Assert.All(updated.FileReferences.Cast<FileReference>(),
                    reference => Assert.False(string.IsNullOrWhiteSpace(reference.Hash)));
            }
        }

        [Fact]
        public async Task ExecuteAsync_WhenPolicyIsUpdateManifestOnly_SkipsCompositeHandlerAndDependencyPublication()
        {
            await using Harness harness = new();
            FileInfo child = harness.CreateFile("Child.application");
            FileInfo app = harness.CreateManifest("App.manifest", child.Name);
            FileInfo output = new(Path.Combine(harness.Source.FullName, "out", app.Name));
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            harness.Pipeline = harness.CreatePipeline(harness.Reader, payloadHandler: handler);

            await harness.ExecuteAsync(app, output, policy: ClickOnceApplicationSigningPolicy.UpdateManifestOnly);

            Assert.Empty(handler.ReceivedCalls());
            Assert.Equal("payload", File.ReadAllText(child.FullName));
            Assert.Single(output.Directory!.EnumerateFiles());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_WhenManifestUpdatesAreDisabled_SignsOriginalBytesWithoutPayloadDiscovery(bool outOfPlace)
        {
            await using Harness harness = new();
            FileInfo app = harness.CreateManifest("App.manifest", @"missing\Child.application");
            string original = File.ReadAllText(app.FullName);
            FileInfo output = outOfPlace
                ? new FileInfo(Path.Combine(harness.Source.FullName, "out", app.Name))
                : app;
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            harness.Pipeline = harness.CreatePipeline(harness.Reader, payloadHandler: handler);

            await harness.ExecuteAsync(app, output, updateManifest: false);

            Assert.Equal(original + "\n<!--signed-->", File.ReadAllText(output.FullName));
            if (outOfPlace)
            {
                Assert.Equal(original, File.ReadAllText(app.FullName));
            }
            Assert.Empty(handler.ReceivedCalls());
            Assert.Empty(harness.PayloadCalls);
            Assert.Single(harness.ManifestCalls);
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData("payload.dll", "payload.dll", false, false)]
        [InlineData("payload.dll", "payload.dll", true, false)]
        [InlineData("payload.dll", "payload.dll.deploy", true, false)]
        [InlineData("payload.deploy", "payload.deploy", true, false)]
        [InlineData("payload.deploy", "payload.deploy.deploy", true, false)]
        [InlineData("payload.dll", "payload.dll.deploy", true, true)]
        public async Task ExecuteAsync_WhenUpdateOnlyHasExplicitPayloadResult_UsesPrimaryForHashesWithoutPublishingDependencies(
            string target, string physical, bool outOfPlace, bool container)
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile(physical, "unsigned");
            FileInfo unselected = harness.CreateFile("unselected.txt", "read only");
            unselected.IsReadOnly = true;
            FileInfo app = harness.CreateManifest("App.manifest", target, unselected.Name);
            SigningFile input = container
                ? SigningFile.ContainerEntry(app,
                    SigningSourceIdentity.ContainerEntry(
                        SigningSourceIdentity.Capture(harness.CreateFile("outer.zip")), "inner.zip"),
                    "app/App.manifest")
                : SigningFile.Capture(app);
            SigningFile payloadInput = input.GetSibling(payload, harness.Source);
            FileInfo signed = harness.CreateFile(@"signed\primary.bin", "signed explicit");
            harness.CreateFile(@"signed\App.manifest", "must not overwrite app");
            harness.CreateFile(@"signed\companion.bin", "must not publish");
            int owners = 0;
            SigningOperationResult result = await harness.Coordinator.ExecuteArtifactAsync(
                payloadInput.SourceIdentity, () =>
                {
                    ++owners;
                    return Task.FromResult(SigningOperationArtifact.Layout(signed, signed.Directory!));
                });
            FileInfo explicitOutput = outOfPlace
                ? new FileInfo(Path.Combine(harness.Source.FullName, "explicit-out", physical))
                : payload;
            result.MaterializePrimary(explicitOutput);
            FileInfo output = outOfPlace
                ? new FileInfo(Path.Combine(harness.Source.FullName, "app-out", app.Name))
                : app;
            List<SigningSourceIdentity> identities = new() { payloadInput.SourceIdentity };
            ClickOnceApplicationSigningContext context = new(identities);
            identities.Clear();
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            harness.Pipeline = harness.CreatePipeline(harness.Reader, payloadHandler: handler);

            await harness.Pipeline.ExecuteAsync(new SigningOperationPlan(input, output),
                harness.Options(), harness.Coordinator, ClickOnceApplicationSigningPolicy.UpdateManifestOnly,
                context: context);

            Assert.Equal(1, owners);
            Assert.Equal(outOfPlace ? "unsigned" : "signed explicit", File.ReadAllText(payload.FullName));
            Assert.Equal("signed explicit", File.ReadAllText(explicitOutput.FullName));
            Assert.Equal("read only", File.ReadAllText(unselected.FullName));
            Assert.True(File.GetAttributes(unselected.FullName).HasFlag(FileAttributes.ReadOnly));
            Assert.Empty(handler.ReceivedCalls());
            Assert.Empty(harness.PayloadCalls);
            Assert.Single(harness.ManifestCalls);
            if (outOfPlace)
            {
                Assert.Single(output.Directory!.EnumerateFiles("*", SearchOption.AllDirectories));
            }
            using FileStream stream = output.OpenRead();
            Assert.True(harness.Reader.TryReadApplicationManifest(stream, out IApplicationManifest? updated));
            using (updated)
            {
                FileReference reference = updated!.FileReferences.Cast<FileReference>()
                    .Single(reference => reference.TargetPath == target);
                Assert.Equal(Convert.ToBase64String(SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes("signed explicit"))), reference.Hash);
            }
            AssertNoStagingDirectories(harness);
            unselected.IsReadOnly = false;
        }

        [Fact]
        public async Task ExecuteAsync_WhenUpdateOnlyHasPendingExplicitPayloads_WaitsForAllBeforeIdentityAndMetadata()
        {
            await using Harness harness = new();
            string[] targets = { "first.dll", "second.dll", "third.dll" };
            FileInfo[] payloads = targets.Select(target => harness.CreateFile(target)).ToArray();
            FileInfo app = harness.CreateManifest("App.manifest", targets);
            IApplicationManifest manifest = harness.UseFakeManifest("App.manifest", targets);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.AfterResolve = _ => entered.TrySetResult();
            TaskCompletionSource<FileInfo>[] completions = payloads.Select(_ =>
                new TaskCompletionSource<FileInfo>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            FileInfo released = harness.CreateFile("released.bin", "signed");
            Task<SigningOperationResult>[] prerequisites = payloads.Select((payload, index) =>
                harness.Coordinator.ExecuteAsync(SigningSourceIdentity.Capture(payload),
                    () => completions[index].Task)).ToArray();
            List<string> order = new();
            manifest.When(value => value.SynchronizeAssemblyIdentities()).Do(_ =>
            {
                Assert.All(prerequisites, task => Assert.True(task.IsCompletedSuccessfully));
                Assert.All(harness.FakeReferences,
                    reference => Assert.Equal("signed", File.ReadAllText(reference.ResolvedPath!)));
                order.Add("identity");
            });
            harness.BeforeUpdate = () => order.Add("update");
            harness.BeforeManifestSign = _ => order.Add("sign");
            ClickOnceApplicationSigningContext context = new(
                payloads.Select(SigningSourceIdentity.Capture));
            FileInfo output = new(Path.Combine(harness.Source.FullName, "out", app.Name));
            Task operation = harness.ExecuteAsync(app, output,
                policy: ClickOnceApplicationSigningPolicy.UpdateManifestOnly, context: context);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Empty(order);
                completions[2].SetResult(harness.CreateFile("third-signed.bin", "signed"));
                await prerequisites[2].WaitAsync(TimeSpan.FromSeconds(5));
                completions[0].SetResult(harness.CreateFile("first-signed.bin", "signed"));
                await prerequisites[0].WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Empty(order);
            }
            finally
            {
                foreach (TaskCompletionSource<FileInfo> completion in completions)
                {
                    completion.TrySetResult(released);
                }
            }
            await operation.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(new[] { "identity", "update", "sign" }, order);
            Assert.Empty(harness.PayloadCalls);
            Assert.Single(output.Directory!.EnumerateFiles());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_WhenUpdateOnlyPrerequisiteFails_OwnerAndWaitersObserveSameException(bool cancellation)
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile("payload.dll");
            FileInfo app = harness.CreateManifest("App.manifest", payload.Name);
            string original = File.ReadAllText(app.FullName);
            TaskCompletionSource<FileInfo> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<SigningOperationResult> prerequisite = harness.Coordinator.ExecuteAsync(
                SigningSourceIdentity.Capture(payload), () => completion.Task);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.UseFakeManifest("App.manifest", payload.Name);
            harness.AfterResolve = _ => entered.TrySetResult();
            ClickOnceApplicationSigningContext context = new(new[] { SigningSourceIdentity.Capture(payload) });
            Task owner = harness.ExecuteAsync(app,
                policy: ClickOnceApplicationSigningPolicy.UpdateManifestOnly, context: context);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task waiter = harness.ExecuteAsync(app,
                policy: ClickOnceApplicationSigningPolicy.UpdateManifestOnly, context: context);
            Exception failure = cancellation ? new OperationCanceledException("explicit payload") :
                new SigningException("explicit payload");
            completion.SetException(failure);

            foreach (Task task in new Task[] { prerequisite, owner, waiter })
            {
                Exception actual = await Assert.ThrowsAnyAsync<Exception>(
                    () => task.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Same(failure, actual);
            }
            Assert.Empty(harness.ManifestCalls);
            Assert.Equal(original, File.ReadAllText(app.FullName));
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public async Task ExecuteAsync_WhenUpdateOnlyExplicitPayloadIsUnregistered_RejectsWithoutClaimingPayload()
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile("payload.dll");
            FileInfo app = harness.CreateManifest("App.manifest", payload.Name);
            ClickOnceApplicationSigningContext context = new(new[] { SigningSourceIdentity.Capture(payload) });

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => harness.ExecuteAsync(app, policy: ClickOnceApplicationSigningPolicy.UpdateManifestOnly,
                    context: context));

            Assert.Contains(payload.FullName, failure.Message, StringComparison.Ordinal);
            Assert.Contains("not registered", failure.Message, StringComparison.Ordinal);
            Assert.False(harness.Coordinator.TryGetResult(SigningSourceIdentity.Capture(payload), out _));
            Assert.Empty(harness.PayloadCalls);
            Assert.Empty(harness.ManifestCalls);
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public async Task ExecuteAsync_WhenUpdateOnlyResultIsNotInExplicitContext_HashesOriginalPayloadBytes()
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile("payload.dll", "unsigned");
            FileInfo app = harness.CreateManifest("App.manifest", payload.Name);
            await harness.Coordinator.ExecuteAsync(SigningSourceIdentity.Capture(payload),
                () => Task.FromResult(harness.CreateFile("signed.bin", "signed")));

            await harness.ExecuteAsync(app, policy: ClickOnceApplicationSigningPolicy.UpdateManifestOnly,
                context: new ClickOnceApplicationSigningContext(Array.Empty<SigningSourceIdentity>()));

            using FileStream stream = app.OpenRead();
            Assert.True(harness.Reader.TryReadApplicationManifest(stream, out IApplicationManifest? updated));
            using (updated)
            {
                Assert.Equal(Convert.ToBase64String(SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes("unsigned"))),
                    Assert.Single(updated!.FileReferences.Cast<FileReference>()).Hash);
            }
            Assert.Empty(harness.PayloadCalls);
        }

        [Fact]
        public async Task ExecuteAsync_WhenManifestUpdatesAreDisabled_IgnoresUnregisteredExplicitContext()
        {
            await using Harness harness = new();
            FileInfo app = harness.CreateManifest("App.manifest", "missing.dll");
            SigningSourceIdentity missing = SigningSourceIdentity.Capture(
                new FileInfo(Path.Combine(harness.Source.FullName, "missing.dll")));
            string original = File.ReadAllText(app.FullName);

            await harness.ExecuteAsync(app, updateManifest: false,
                context: new ClickOnceApplicationSigningContext(new[] { missing }));

            Assert.False(harness.Coordinator.TryGetResult(missing, out _));
            Assert.Equal(original + "\n<!--signed-->", File.ReadAllText(app.FullName));
            Assert.Empty(harness.PayloadCalls);
            Assert.Single(harness.ManifestCalls);
        }

        [Fact]
        public async Task ExecuteAsync_WhenUpdateOnlyDependenciesFormCycle_FaultsEveryOwnerAndWaiterWithoutHanging()
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile("payload.dll");
            FileInfo app = harness.CreateManifest("App.manifest", payload.Name);
            TaskCompletionSource startDependency = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<SigningOperationResult> prerequisite = harness.Coordinator.ExecuteAsync(
                SigningSourceIdentity.Capture(payload), async () =>
                {
                    await startDependency.Task;
                    Assert.True(harness.Coordinator.TryGetResult(SigningSourceIdentity.Capture(app),
                        out Task<SigningOperationResult>? appTask));
                    await harness.Coordinator.WaitAsync(appTask!);
                    return payload;
                });
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.UseFakeManifest("App.manifest", payload.Name);
            harness.AfterResolve = _ => entered.TrySetResult();
            ClickOnceApplicationSigningContext context = new(new[] { SigningSourceIdentity.Capture(payload) });
            Task owner = harness.ExecuteAsync(app,
                policy: ClickOnceApplicationSigningPolicy.UpdateManifestOnly, context: context);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task waiter = harness.ExecuteAsync(app,
                policy: ClickOnceApplicationSigningPolicy.UpdateManifestOnly, context: context);
            startDependency.SetResult();

            InvalidOperationException expected = await Assert.ThrowsAsync<InvalidOperationException>(
                () => prerequisite.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("cycle", expected.Message, StringComparison.OrdinalIgnoreCase);
            foreach (Task task in new[] { owner, waiter })
            {
                InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => task.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Same(expected, actual);
            }
            Assert.Empty(harness.PayloadCalls);
            Assert.Empty(harness.ManifestCalls);
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public async Task ExecuteAsync_WhenUpdateOnlyApplicationsSharePendingPayload_ReusesOnePayloadOwnerConcurrently()
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile("payload.dll");
            FileInfo firstApp = harness.CreateManifest("First.manifest", payload.Name);
            FileInfo secondApp = harness.CreateManifest("Second.manifest", payload.Name);
            TaskCompletionSource<FileInfo> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int owners = 0;
            Task<SigningOperationResult> prerequisite = harness.Coordinator.ExecuteAsync(
                SigningSourceIdentity.Capture(payload), () =>
                {
                    ++owners;
                    return completion.Task;
                });
            ClickOnceApplicationSigningContext context = new(new[] { SigningSourceIdentity.Capture(payload) });
            Task first = harness.ExecuteAsync(firstApp,
                policy: ClickOnceApplicationSigningPolicy.UpdateManifestOnly, context: context);
            Task second = harness.ExecuteAsync(secondApp,
                policy: ClickOnceApplicationSigningPolicy.UpdateManifestOnly, context: context);
            completion.SetResult(harness.CreateFile("signed.bin", "signed"));

            await Task.WhenAll(first, second, prerequisite).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, owners);
            Assert.Equal(2, harness.ManifestCalls.Count);
            Assert.Empty(harness.PayloadCalls);
            Assert.Equal("payload", File.ReadAllText(payload.FullName));
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public void Constructor_WhenExplicitIdentitiesAreNullOrContainNull_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ClickOnceApplicationSigningContext(null!));
            Assert.Throws<ArgumentException>(() =>
                new ClickOnceApplicationSigningContext(new SigningSourceIdentity[] { null! }));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_WhenManifestUpdatesAreDisabledAndInputIsInvalid_RejectsBeforeSigning(bool malformed)
        {
            await using Harness harness = new();
            FileInfo file = malformed
                ? harness.CreateFile("App.manifest", "<assembly>")
                : ClickOnceResolutionTestUtilities.WriteDeploymentManifest(harness.Source,
                    "App.manifest", "missing.manifest");
            string original = File.ReadAllText(file.FullName);

            await Assert.ThrowsAsync<SigningException>(() => harness.ExecuteAsync(file, updateManifest: false));

            Assert.Equal(original, File.ReadAllText(file.FullName));
            Assert.Empty(harness.ManifestCalls);
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_WhenComposedHandlerFails_OwnerAndWaiterObserveSameException(bool cancellation)
        {
            await using Harness harness = new();
            FileInfo child = harness.CreateFile("Child.application");
            FileInfo app = harness.CreateManifest("App.manifest", child.Name);
            string original = File.ReadAllText(app.FullName);
            Exception failure = cancellation ? new OperationCanceledException("child") : new SigningException("child");
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<ClickOncePayloadSigningResult> completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            handler.CanSign(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>()).Returns(true);
            handler.SignAsync(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>(),
                Arg.Any<SigningOperationCoordinator>()).Returns(_ =>
                {
                    entered.SetResult();
                    return completion.Task;
                });
            harness.Pipeline = harness.CreatePipeline(harness.Reader, payloadHandler: handler);
            Task owner = harness.ExecuteAsync(app);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task waiter = harness.ExecuteAsync(app);
            completion.SetException(failure);

            foreach (Task operation in new[] { owner, waiter })
            {
                Exception actual = await Assert.ThrowsAnyAsync<Exception>(
                    () => operation.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Same(failure, actual);
            }
            Assert.Equal(original, File.ReadAllText(app.FullName));
            Assert.Empty(harness.ManifestCalls);
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_WhenChildOutputPlansConflict_RejectsBeforeCopying(bool directoryConflict)
        {
            await using Harness harness = new();
            harness.CreateFile("Child.application");
            FileInfo app = harness.CreateManifest("App.manifest", "Child.application");
            string? firstDestination = null;
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            handler.CanSign(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>()).Returns(true);
            handler.SignAsync(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>(),
                Arg.Any<SigningOperationCoordinator>()).Returns(async call =>
                {
                    ClickOncePayloadSigningRequest request = call.Arg<ClickOncePayloadSigningRequest>();
                    FileInfo first = harness.CreateFile("first.bin", "first");
                    FileInfo second = harness.CreateFile("second.bin", "second");
                    SigningOperationResult primary = await harness.Coordinator.ExecuteAsync(
                        request.Source.SourceIdentity, () => Task.FromResult(harness.CreateFile("signed.bin")));
                    SigningOperationResult firstResult = await harness.Coordinator.ExecuteAsync(
                        SigningSourceIdentity.Capture(first), () => Task.FromResult(first));
                    SigningOperationResult secondResult = await harness.Coordinator.ExecuteAsync(
                        SigningSourceIdentity.Capture(second), () => Task.FromResult(second));
                    FileInfo destination = new(Path.Combine(request.StagingRoot.FullName, "companion"));
                    firstDestination = destination.FullName;
                    FileInfo conflicting = directoryConflict
                        ? new FileInfo(Path.Combine(destination.FullName, "nested.bin"))
                        : new FileInfo(destination.FullName.ToUpperInvariant());
                    return new ClickOncePayloadSigningResult(primary, new[]
                    {
                        new ClickOncePayloadSigningOutput(new SigningOperationPlan(SigningFile.Capture(first), destination),
                            firstResult),
                        new ClickOncePayloadSigningOutput(new SigningOperationPlan(SigningFile.Capture(second), conflicting),
                            secondResult)
                    });
                });
            harness.Pipeline = harness.CreatePipeline(harness.Reader, payloadHandler: handler);

            await Assert.ThrowsAsync<ClickOnceStagingException>(() => harness.ExecuteAsync(app));

            Assert.False(File.Exists(firstDestination));
            Assert.Empty(harness.ManifestCalls);
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public async Task ExecuteAsync_WhenManifestUpdatesAreDisabled_UsesTypedReadWithoutMutationOrSerialization()
        {
            await using Harness harness = new();
            FileInfo app = harness.CreateManifest("App.manifest", "missing.dll");
            string original = File.ReadAllText(app.FullName);
            IApplicationManifest manifest = harness.UseFakeManifest("App.manifest", "missing.dll");
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            harness.Pipeline = harness.CreatePipeline(harness.Reader, payloadHandler: handler);

            await harness.ExecuteAsync(app, updateManifest: false);

            manifest.DidNotReceive().ReadOnly = false;
            manifest.DidNotReceive().ResolveFiles(Arg.Any<IReadOnlyList<DirectoryInfo>>());
            manifest.DidNotReceive().SynchronizeAssemblyIdentities();
            manifest.DidNotReceive().UpdateFileInfo();
            manifest.DidNotReceive().Write(Arg.Any<FileInfo>());
            manifest.Received(1).Dispose();
            Assert.Empty(handler.ReceivedCalls());
            Assert.Equal(original + "\n<!--signed-->", File.ReadAllText(app.FullName));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ExecuteAsync_WhenSigningVstoManifest_PreservesAddInFieldsAndSigningOptions(bool updateManifest)
        {
            await using Harness harness = new();
            FileInfo app = harness.CreateFile("Vsto.manifest",
                File.ReadAllText(ClickOnceResolutionTestUtilities.GetVstoManifestPath()));
            string original = File.ReadAllText(app.FullName);
            SignOptions options = new("App name", "Publisher", "Description", new Uri("https://example.test/support"),
                HashAlgorithmName.SHA256, HashAlgorithmName.SHA256,
                new Uri("https://example.test/timestamp"), null, null, recurseContainers: true);
            if (updateManifest)
            {
                using FileStream stream = app.OpenRead();
                Assert.True(harness.Reader.TryReadApplicationManifest(stream, out IApplicationManifest? manifest));
                using (manifest)
                {
                    foreach (BaseReference reference in ClickOncePayloadResolver.GetPhysicalReferences(manifest!))
                    {
                        FileInfo payload = harness.CopyAssembly(reference.TargetPath);
                        payload.Directory!.Create();
                    }
                }
            }

            await harness.ExecuteAsync(app, options: options, updateManifest: updateManifest);

            harness.ManifestSigner.Received(1).Sign(Arg.Any<FileInfo>(),
                Arg.Any<System.Security.Cryptography.X509Certificates.X509Certificate2>(), Arg.Any<RSA>(),
                Arg.Is<SignOptions>(actual => ReferenceEquals(actual, options)));
            XDocument before = XDocument.Parse(original);
            XDocument after = XDocument.Load(app.FullName);
            Assert.True(XNode.DeepEquals(
                before.Descendants(XName.Get("addIn", "urn:schemas-microsoft-com:vsta.v3")).Single(),
                after.Descendants(XName.Get("addIn", "urn:schemas-microsoft-com:vsta.v3")).Single()));
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData(0, "Requested")]
        [InlineData(0, "Requested.exe")]
        [InlineData(1, "Requested")]
        [InlineData(1, "Requested.exe")]
        public async Task ExecuteAsync_WhenUpdatingWithApplicationNameOverride_AppliesNameLiterally(
            int policyValue, string name)
        {
            ClickOnceApplicationSigningPolicy policy = (ClickOnceApplicationSigningPolicy)policyValue;
            await using Harness harness = new();
            FileInfo app = harness.CreateManifest("App.manifest");
            SignOptions options = new(name, null, null, null,
                HashAlgorithmName.SHA256, HashAlgorithmName.SHA256, null!, null, null, true);
            harness.BeforeManifestSign = file => Assert.Equal(name,
                XDocument.Load(file.FullName).Root!.Elements()
                    .Single(element => element.Name.LocalName == "assemblyIdentity").Attribute("name")!.Value);

            await harness.ExecuteAsync(app, options: options, policy: policy);

            using FileStream stream = app.OpenRead();
            Assert.True(harness.Reader.TryReadApplicationManifest(stream, out IApplicationManifest? updated));
            using (updated)
            {
                Assert.Equal(name, updated!.AssemblyIdentity.Name);
            }
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData(0, null)]
        [InlineData(0, "")]
        [InlineData(0, " \t ")]
        [InlineData(1, null)]
        [InlineData(1, "")]
        [InlineData(1, " \t ")]
        public async Task ExecuteAsync_WhenUpdatingWithoutNonWhitespaceNameOverride_PreservesOriginalName(
            int policyValue, string? name)
        {
            ClickOnceApplicationSigningPolicy policy = (ClickOnceApplicationSigningPolicy)policyValue;
            await using Harness harness = new();
            FileInfo app = harness.CreateManifest("App.manifest");
            XDocument original = XDocument.Load(app.FullName);
            string originalName = original.Root!.Elements()
                .Single(element => element.Name.LocalName == "assemblyIdentity").Attribute("name")!.Value;
            SignOptions options = new(name, null, null, null,
                HashAlgorithmName.SHA256, HashAlgorithmName.SHA256, null!, null, null, true);

            await harness.ExecuteAsync(app, options: options, policy: policy);

            using FileStream stream = app.OpenRead();
            Assert.True(harness.Reader.TryReadApplicationManifest(stream, out IApplicationManifest? updated));
            using (updated)
            {
                Assert.Equal(originalName, updated!.AssemblyIdentity.Name);
            }
        }

        [Fact]
        public async Task ExecuteAsync_WhenManifestUpdatesAreDisabled_IgnoresNameOverrideAndSignsOriginalBytes()
        {
            await using Harness harness = new();
            FileInfo app = harness.CreateManifest("App.manifest", "missing.dll");
            byte[] original = File.ReadAllBytes(app.FullName);
            IApplicationManifest manifest = harness.UseFakeManifest("App.manifest", "missing.dll");
            AssemblyIdentity identity = new() { Name = "Original" };
            manifest.AssemblyIdentity.Returns(identity);
            SignOptions options = new("Requested.exe", null, null, null,
                HashAlgorithmName.SHA256, HashAlgorithmName.SHA256, null!, null, null, true);
            harness.BeforeManifestSign = file => Assert.Equal(original, File.ReadAllBytes(file.FullName));

            await harness.ExecuteAsync(app, options: options, updateManifest: false);

            Assert.Equal("Original", identity.Name);
            manifest.DidNotReceive().Write(Arg.Any<FileInfo>());
            manifest.DidNotReceive().SynchronizeAssemblyIdentities();
            manifest.DidNotReceive().UpdateFileInfo();
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task ExecuteAsync_WhenVstoApplicationNameIsOverridden_PreservesVstoFieldsAndPayloadIdentity(
            int policyValue)
        {
            ClickOnceApplicationSigningPolicy policy = (ClickOnceApplicationSigningPolicy)policyValue;
            await using Harness harness = new();
            XDocument original = XDocument.Load(ClickOnceResolutionTestUtilities.GetVstoManifestPath());
            XElement description = original.Descendants(XName.Get("description", "urn:schemas-microsoft-com:asm.v1"))
                .Single();
            description.SetAttributeValue(XName.Get("supportUrl", "urn:schemas-microsoft-com:asm.v2"),
                "https://original.test/support");
            FileInfo app = harness.CreateFile("Vsto.manifest", original.ToString());
            FileInfo payload = harness.CopyAssembly("payload.dll");
            SignOptions options = new("Requested.exe", "Ignored deployment publisher", "Ignored deployment description",
                new Uri("https://requested.test/support"), HashAlgorithmName.SHA256, HashAlgorithmName.SHA256,
                null!, null, null, true);

            await harness.ExecuteAsync(app, options: options, policy: policy);

            XDocument updated = XDocument.Load(app.FullName);
            Assert.Equal("Requested.exe", updated.Root!.Elements()
                .Single(element => element.Name.LocalName == "assemblyIdentity").Attribute("name")!.Value);
            XElement updatedDescription = updated.Descendants(
                XName.Get("description", "urn:schemas-microsoft-com:asm.v1")).Single();
            Assert.Equal(description.Value, updatedDescription.Value);
            Assert.Equal(
                description.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration)
                    .OrderBy(attribute => attribute.Name.ToString()).Select(attribute => (attribute.Name, attribute.Value)),
                updatedDescription.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration)
                    .OrderBy(attribute => attribute.Name.ToString()).Select(attribute => (attribute.Name, attribute.Value)));
            Assert.True(XNode.DeepEquals(
                original.Descendants(XName.Get("addIn", "urn:schemas-microsoft-com:vsta.v3")).Single(),
                updated.Descendants(XName.Get("addIn", "urn:schemas-microsoft-com:vsta.v3")).Single()));
            using FileStream stream = app.OpenRead();
            Assert.True(harness.Reader.TryReadApplicationManifest(stream, out IApplicationManifest? manifest));
            using (manifest)
            {
                Assert.Equal(AssemblyIdentity.FromManagedAssembly(payload.FullName)!.Name,
                    Assert.Single(ClickOncePayloadResolver.GetPhysicalReferences(manifest!)
                        .OfType<AssemblyReference>()).AssemblyIdentity.Name);
            }
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(0, false)]
        public async Task ExecuteAsync_WhenRealSigningUsesApplicationNameOption_SignatureCoversEffectiveNameWithoutTimestamp(
            int policyValue, bool updateManifest)
        {
            ClickOnceApplicationSigningPolicy policy = (ClickOnceApplicationSigningPolicy)policyValue;
            await using Harness harness = new();
            using X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate();
            harness.CertificateProvider.GetCertificateAsync()
                .Returns(_ => Task.FromResult(new X509Certificate2(certificate)));
            harness.SignatureProvider.GetRsaAsync()
                .Returns(_ => Task.FromResult(certificate.GetRSAPrivateKey()!));
            harness.ManifestSigner = new ManifestSigner();
            harness.Pipeline = harness.CreatePipeline(harness.Reader);
            FileInfo app = harness.CreateManifest("App.manifest");
            SignOptions options = new("Requested.exe", null, null, null,
                HashAlgorithmName.SHA256, HashAlgorithmName.SHA256, null!, null, null, true);
            string originalName = XDocument.Load(app.FullName).Root!.Elements()
                .Single(element => element.Name.LocalName == "assemblyIdentity").Attribute("name")!.Value;

            await harness.ExecuteAsync(app, options: options, policy: policy, updateManifest: updateManifest);

            XmlDocument document = new() { PreserveWhitespace = true };
            document.Load(app.FullName);
            XmlElement identity = document.DocumentElement!.ChildNodes.OfType<XmlElement>()
                .Single(element => element.LocalName == "assemblyIdentity");
            string expectedName = updateManifest ? "Requested.exe" : originalName;
            Assert.Equal(expectedName, identity.GetAttribute("name"));
            XmlElement licenseIdentity = Assert.Single(document.GetElementsByTagName(
                "assemblyIdentity", "http://schemas.microsoft.com/windows/pki/2005/Authenticode")
                .Cast<XmlElement>());
            Assert.Equal(expectedName, licenseIdentity.GetAttribute("name"));
            XmlElement signature = document.DocumentElement.ChildNodes.OfType<XmlElement>()
                .Single(element => element.LocalName == "Signature" &&
                    element.NamespaceURI == SignedXml.XmlDsigNamespaceUrl);
            SignedXml signed = new(document);
            signed.LoadXml(signature);
            Assert.True(signed.CheckSignature(certificate, verifySignatureOnly: true));
            Assert.Empty(document.GetElementsByTagName(
                "Timestamp", "http://schemas.microsoft.com/windows/pki/2005/Authenticode").Cast<XmlElement>());
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public async Task MaterializePrimary_WhenResultContainsLayoutSiblings_CopiesOnlyPrimaryAndPreservesFullMaterializationAndLifetimeGuard()
        {
            await using Harness harness = new();
            FileInfo primary = harness.CreateFile(@"signed\Primary.application", "primary");
            harness.CreateFile(@"signed\Dependency.dll", "dependency");
            SigningOperationResult result = await harness.Coordinator.ExecuteArtifactAsync(
                SigningSourceIdentity.Capture(primary),
                () => Task.FromResult(SigningOperationArtifact.Layout(primary, primary.Directory!)));
            FileInfo primaryOnly = new(Path.Combine(harness.Source.FullName, "primary-only", primary.Name));
            FileInfo full = new(Path.Combine(harness.Source.FullName, "full", primary.Name));

            result.MaterializePrimary(primaryOnly);
            result.Materialize(full);

            Assert.Equal("primary", File.ReadAllText(primaryOnly.FullName));
            Assert.Single(primaryOnly.Directory!.EnumerateFiles());
            Assert.Equal("dependency", File.ReadAllText(Path.Combine(full.DirectoryName!, "Dependency.dll")));
            await harness.Coordinator.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => result.MaterializePrimary(primaryOnly));
        }
    }
}
