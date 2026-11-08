// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.FileSystemGlobbing;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed partial class ClickOnceApplicationManifestSignerTests
    {
        [Theory]
        [InlineData("Payload.dll", "payload.dll", false, false)]
        [InlineData("Payload.dll.deploy", "payload.dll", false, false)]
        [InlineData("Payload.deploy", "payload.deploy", false, false)]
        [InlineData("Payload.deploy.deploy", "payload.deploy", false, false)]
        [InlineData("Payload.dll", "payload.dll", true, false)]
        [InlineData("Payload.dll.deploy", "payload.dll", true, false)]
        [InlineData("Payload.deploy", "payload.deploy", true, false)]
        [InlineData("Payload.deploy.deploy", "payload.deploy", true, false)]
        [InlineData("Payload.dll", "payload.dll", false, true)]
        [InlineData("Payload.dll.deploy", "payload.dll", false, true)]
        [InlineData("Payload.deploy", "payload.deploy", false, true)]
        [InlineData("Payload.deploy.deploy", "payload.deploy", false, true)]
        [InlineData("Payload.dll", "payload.dll", true, true)]
        [InlineData("Payload.dll.deploy", "payload.dll", true, true)]
        [InlineData("Payload.deploy", "payload.deploy", true, true)]
        [InlineData("Payload.deploy.deploy", "payload.deploy", true, true)]
        public async Task ExecuteAsync_WhenSelectionUsesOriginalCase_PreservesSpellingAndExcludedMetadata(
            string physical, string target, bool excluded, bool outOfPlace)
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile(Path.Combine("Lib", physical), "unsigned");
            FileInfo unsupported = harness.CreateFile("Unsupported.txt", "unsupported");
            string targetPath = Path.Combine("lib", target);
            FileInfo app = harness.CreateManifest("App.manifest", targetPath, unsupported.Name);
            harness.FormatSigner.CanSign(Arg.Any<FileInfo>()).Returns(call =>
                ((FileInfo)call[0]).Extension is ".dll" or ".deploy");
            Matcher matching = new(StringComparison.Ordinal);
            matching.AddInclude($"Lib/{physical}");
            FileInfo output = outOfPlace
                ? new FileInfo(Path.Combine(harness.Source.FullName, "out", app.Name))
                : app;

            await harness.ExecuteAsync(app, output,
                options: harness.Options(excluded ? null : matching, excluded ? matching : null));

            Assert.Equal(excluded ? 0 : 1, harness.PayloadCalls.Count);
            Assert.Equal(outOfPlace || excluded ? "unsigned" : "unsigned signed", File.ReadAllText(payload.FullName));
            Assert.Equal("unsupported", File.ReadAllText(unsupported.FullName));
            AssertReferenceHashes(harness, output, new Dictionary<string, string>
            {
                [targetPath] = excluded ? "unsigned" : "unsigned signed",
                [unsupported.Name] = "unsupported"
            });
            if (outOfPlace)
            {
                Assert.Equal(excluded ? 1 : 2, output.Directory!.EnumerateFiles("*", SearchOption.AllDirectories).Count());
                if (!excluded)
                {
                    string suffix = physical.EndsWith(".deploy", StringComparison.Ordinal) &&
                        physical.Length != target.Length ? ".deploy" : string.Empty;
                    Assert.Equal("unsigned signed", File.ReadAllText(
                        Path.Combine(output.Directory.FullName, targetPath + suffix)));
                }
            }
            AssertNoStagingDirectories(harness);
        }

        [RequiresCaseSensitiveDirectoryTheory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_WhenResolvedStagingPathDiffersOnlyByCase_RejectsBeforeSigningOrMetadataUpdate(
            bool parentDirectory)
        {
            await using Harness harness = new(directory =>
            {
                Assert.Empty(directory.EnumerateFileSystemInfos());
                Assert.True(EnableCaseSensitivity(directory));
            });
            FileInfo payload = harness.CreateFile(@"lib\payload.dll", "unsigned");
            FileInfo app = harness.CreateManifest("App.manifest", @"lib\payload.dll");
            string originalManifest = File.ReadAllText(app.FullName);
            FileInfo output = new(Path.Combine(harness.Source.FullName, "out", app.Name));
            IApplicationManifest fake = harness.UseFakeManifest("App.manifest", @"lib\payload.dll");
            string? expectedPath = null;
            harness.AfterResolve = references =>
            {
                BaseReference reference = Assert.Single(references);
                FileInfo expected = new(reference.SourcePath);
                expectedPath = expected.FullName;
                FileInfo alternate = new(parentDirectory
                    ? Path.Combine(expected.Directory!.Parent!.FullName, "Lib", expected.Name)
                    : Path.Combine(expected.Directory!.FullName, "Payload.dll"));
                alternate.Directory!.Create();
                File.WriteAllText(alternate.FullName, "different unsigned bytes");
                Assert.NotEqual(expected.FullName, alternate.FullName);
                Assert.Equal(expected.FullName, alternate.FullName, ignoreCase: true);
                Assert.Equal("unsigned", File.ReadAllText(expected.FullName));
                Assert.Equal("different unsigned bytes", File.ReadAllText(alternate.FullName));
                reference.ResolvedPath = alternate.FullName;
            };

            Exception? failure = await Record.ExceptionAsync(() => harness.ExecuteAsync(app, output));

            Assert.NotNull(expectedPath);
            SigningException exception = Assert.IsType<SigningException>(failure);
            Assert.Contains(app.FullName, exception.Message, StringComparison.Ordinal);
            Assert.Contains(expectedPath, exception.Message, StringComparison.Ordinal);
            fake.DidNotReceive().SynchronizeAssemblyIdentities();
            fake.DidNotReceive().UpdateFileInfo();
            Assert.Empty(harness.PayloadCalls);
            Assert.Empty(harness.ManifestCalls);
            Assert.Equal(originalManifest, File.ReadAllText(app.FullName));
            Assert.Equal("unsigned", File.ReadAllText(payload.FullName));
            Assert.False(File.Exists(output.FullName));
            AssertNoStagingDirectories(harness);
        }

        [RequiresCaseSensitiveDirectoryTheory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task SignOwnerAsync_WhenCaseSensitivePayloadCollidesWithPrimary_RejectsBeforeCopying(int mode)
        {
            await using Harness harness = new();
            Assert.True(EnableCaseSensitivity(harness.Source));
            FileInfo payload = harness.CreateFile("app.manifest", "original payload");
            FileInfo app = harness.CreateManifest("App.manifest", payload.Name);
            string originalManifest = File.ReadAllText(app.FullName);
            using FileStream stream = app.OpenRead();
            Assert.True(harness.Reader.TryReadApplicationManifest(stream, out IApplicationManifest? manifest));
            FileReference reference = Assert.Single(manifest!.FileReferences.Cast<FileReference>());
            ResolvedClickOncePublishLayout layout = new(null,
                new ResolvedClickOnceApplication(app, manifest,
                    new[] { new ResolvedClickOncePayload(payload, reference) }),
                Array.Empty<ResolvedClickOnceAdjacentExecutable>(), Array.Empty<ClickOnceManifestDiagnostic>());

            await Assert.ThrowsAsync<ClickOnceStagingException>(() =>
                harness.Pipeline.SignOwnerAsync(OriginalInput(harness, app, container: true),
                    harness.Options(), harness.Coordinator,
                    mode == 0 ? ClickOnceApplicationSigningPolicy.SignPayloads
                        : ClickOnceApplicationSigningPolicy.UpdateManifestOnly,
                    layout));

            Assert.Empty(harness.PayloadCalls);
            Assert.Empty(harness.ManifestCalls);
            Assert.Equal(originalManifest, File.ReadAllText(app.FullName));
            Assert.Equal("original payload", File.ReadAllText(payload.FullName));
            AssertNoStagingDirectories(harness);
        }

        [RequiresCaseSensitiveDirectoryTheory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task ExecuteAsync_WhenChildOutputRootIsCaseDistinct_RejectsBeforeMaterialization(
            bool ancestorAlias, bool existingOutsideRoot)
        {
            await using Harness harness = new(directory => Assert.True(EnableCaseSensitivity(directory)));
            FileInfo child = harness.CreateFile("Child.application", "unsigned");
            FileInfo app = harness.CreateManifest("App.manifest", child.Name);
            string originalManifest = File.ReadAllText(app.FullName);
            DirectoryInfo parent = harness.Source.CreateSubdirectory("StageParent");
            Assert.True(EnableCaseSensitivity(parent));
            DirectoryInfo stage = parent.CreateSubdirectory("Stage");
            DirectoryInfo outsideRoot = new(ancestorAlias
                ? Path.Combine(harness.Source.FullName, "stageparent", stage.Name)
                : Path.Combine(parent.FullName, "stage"));
            if (existingOutsideRoot)
            {
                outsideRoot.Create();
            }
            FileInfo outside = new(Path.Combine(outsideRoot.FullName, "companion.bin"));
            FileInfo output = new(Path.Combine(harness.Source.FullName, "out", app.Name));
            IDirectoryService directories = Substitute.For<IDirectoryService>();
            directories.CreateTemporaryDirectory().Returns(stage);
            directories.When(service => service.Delete(Arg.Any<DirectoryInfo>())).Do(call =>
                ((DirectoryInfo)call[0]).Delete(recursive: true));
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            handler.CanSign(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>()).Returns(true);
            handler.SignAsync(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>(),
                Arg.Any<SigningOperationCoordinator>()).Returns(async call =>
                {
                    ClickOncePayloadSigningRequest request = call.Arg<ClickOncePayloadSigningRequest>();
                    Assert.Equal(stage.FullName, request.StagingRoot.FullName);
                    FileInfo signed = harness.CreateFile("signed.bin", "signed child");
                    SigningOperationResult result = await harness.Coordinator.WaitAsync(
                        harness.Coordinator.ExecuteAsync(request.Source.SourceIdentity, () => Task.FromResult(signed)));
                    return new ClickOncePayloadSigningResult(result, new[]
                    {
                        new ClickOncePayloadSigningOutput(
                            new SigningOperationPlan(SigningFile.Capture(signed), outside), result)
                    });
                });
            harness.Pipeline = new ClickOnceApplicationManifestSigner(
                new ClickOnceApplicationPublishLayoutResolver(harness.Reader, new ClickOncePayloadResolver()),
                new ClickOncePublishLayoutStager(directories), harness.AggregatingSigner, new FileMatcher(),
                harness.ManifestSigner, harness.CertificateProvider, harness.SignatureProvider, harness.Logger, handler);

            Exception? failure = await Record.ExceptionAsync(() => harness.ExecuteAsync(app, output));

            Assert.False(File.Exists(outside.FullName));
            ClickOnceStagingException stagingFailure = Assert.IsType<ClickOnceStagingException>(failure);
            Assert.Contains(outside.FullName, stagingFailure.Message, StringComparison.Ordinal);
            Assert.Equal(existingOutsideRoot, Directory.Exists(outsideRoot.FullName));
            Assert.Equal(originalManifest, File.ReadAllText(app.FullName));
            Assert.Equal("unsigned", File.ReadAllText(child.FullName));
            Assert.False(File.Exists(output.FullName));
            Assert.Empty(harness.ManifestCalls);
            Assert.False(Directory.Exists(stage.FullName));
        }

        [RequiresCaseSensitiveDirectoryTheory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task ExecuteAsync_WhenSensitiveStageHasCaseVariantCompanions_RetainsFirstSpellingOrRejectsConflictingResults(
            bool lowerFirst, bool differentResults)
        {
            await using Harness harness = new(directory =>
            {
                Assert.Empty(directory.EnumerateFileSystemInfos());
                Assert.True(EnableCaseSensitivity(directory));
            });
            FileInfo child = harness.CreateFile("Child.application", "unsigned");
            FileInfo app = harness.CreateManifest("App.manifest", child.Name);
            string originalManifest = File.ReadAllText(app.FullName);
            FileInfo output = new(Path.Combine(harness.Source.FullName, "out", app.Name));
            string[] names = lowerFirst ? new[] { "meta.bin", "Meta.bin" } : new[] { "Meta.bin", "meta.bin" };
            harness.BeforeManifestSign = file =>
            {
                FileInfo stagedCompanion = Assert.Single(file.Directory!.EnumerateFiles(),
                    entry => string.Equals(entry.Name, "Meta.bin", StringComparison.OrdinalIgnoreCase));
                Assert.Equal(names[0], stagedCompanion.Name);
                Assert.Equal("signed companion", File.ReadAllText(stagedCompanion.FullName));
            };
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            handler.CanSign(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>()).Returns(true);
            handler.SignAsync(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>(),
                Arg.Any<SigningOperationCoordinator>()).Returns(async call =>
                {
                    ClickOncePayloadSigningRequest request = call.Arg<ClickOncePayloadSigningRequest>();
                    FileInfo signedChild = harness.CreateFile(@"signed\Child.application", "signed child");
                    SigningOperationResult primary = await harness.Coordinator.WaitAsync(
                        harness.Coordinator.ExecuteAsync(request.Source.SourceIdentity,
                            () => Task.FromResult(signedChild)));
                    FileInfo companion = harness.CreateFile(@"original\Meta.bin", "signed companion");
                    SigningOperationResult companionResult = await harness.Coordinator.WaitAsync(
                        harness.Coordinator.ExecuteAsync(SigningSourceIdentity.Capture(companion),
                            () => Task.FromResult(companion)));
                    SigningOperationResult secondResult = companionResult;
                    if (differentResults)
                    {
                        FileInfo other = harness.CreateFile(@"signed\Other.bin", "signed companion");
                        secondResult = await harness.Coordinator.WaitAsync(
                            harness.Coordinator.ExecuteAsync(SigningSourceIdentity.Capture(other),
                                () => Task.FromResult(other)));
                        Assert.NotSame(companionResult, secondResult);
                    }
                    return new ClickOncePayloadSigningResult(primary, names.Select((name, index) =>
                        new ClickOncePayloadSigningOutput(
                            new SigningOperationPlan(SigningFile.Capture(companion),
                                new FileInfo(Path.Combine(request.StagingRoot.FullName, name))),
                            index == 0 ? companionResult : secondResult)));
                });
            harness.Pipeline = harness.CreatePipeline(harness.Reader, payloadHandler: handler);

            if (differentResults)
            {
                InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => harness.ExecuteAsync(app, output));
                Assert.Contains("different coordinated results", failure.Message, StringComparison.Ordinal);
                Assert.Empty(harness.ManifestCalls);
                Assert.Equal(originalManifest, File.ReadAllText(app.FullName));
                Assert.Equal("unsigned", File.ReadAllText(child.FullName));
                Assert.False(output.Exists);
                AssertNoStagingDirectories(harness);
                return;
            }
            await harness.ExecuteAsync(app, output);

            Assert.Equal("signed child", File.ReadAllText(Path.Combine(output.DirectoryName!, child.Name)));
            FileInfo publishedCompanion = Assert.Single(output.Directory!.EnumerateFiles(),
                file => string.Equals(file.Name, "Meta.bin", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(lowerFirst ? "meta.bin" : "Meta.bin", publishedCompanion.Name);
            Assert.Equal("signed companion", File.ReadAllText(publishedCompanion.FullName));
            Assert.Equal(3, output.Directory.EnumerateFiles("*", SearchOption.AllDirectories).Count());
            AssertReferenceHashes(harness, output,
                new Dictionary<string, string> { [child.Name] = "signed child" });
            Assert.Equal("unsigned", File.ReadAllText(child.FullName));
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData("Payload.dll", "payload.dll", false)]
        [InlineData("Payload.dll.deploy", "payload.dll", false)]
        [InlineData("Payload.deploy", "payload.deploy", false)]
        [InlineData("Payload.deploy.deploy", "payload.deploy", false)]
        [InlineData("Payload.dll", "payload.dll", true)]
        [InlineData("Payload.dll.deploy", "payload.dll", true)]
        [InlineData("Payload.deploy", "payload.deploy", true)]
        [InlineData("Payload.deploy.deploy", "payload.deploy", true)]
        public async Task ExecuteAsync_WhenMixedCasePayloadIsHandled_PreservesOriginalIdentityParentsAndMappedSuffix(
            string physical, string target, bool container)
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile(Path.Combine("Lib", physical), "unsigned");
            string targetPath = Path.Combine("lib", target);
            FileInfo app = harness.CreateManifest("App.manifest", targetPath);
            SigningFile input = OriginalInput(harness, app, container);
            ClickOncePayloadSigningRequest? capabilityRequest = null;
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            handler.CanSign(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>())
                .Returns(call =>
                {
                    capabilityRequest = call.Arg<ClickOncePayloadSigningRequest>();
                    return true;
                });
            handler.SignAsync(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>(),
                Arg.Any<SigningOperationCoordinator>()).Returns(async call =>
                {
                    ClickOncePayloadSigningRequest request = call.Arg<ClickOncePayloadSigningRequest>();
                    Assert.Same(capabilityRequest, request);
                    Assert.Equal(payload.FullName, request.Source.File.FullName);
                    Assert.Equal(input.GetSibling(payload, harness.Source).SourceIdentity, request.Source.SourceIdentity);
                    Assert.Equal(harness.Source.FullName, request.SourceRoot.FullName);
                    Assert.Equal(payload.FullName, request.StagedFile.Source.FullName);
                    Assert.Equal(targetPath, request.StagedFile.TargetPath);
                    Assert.Equal(target, request.UpdateFile.Name);
                    Assert.Equal(physical.Length != target.Length, request.StagedFile.FileInfoUpdateFile is not null);
                    SigningOperationResult result = await harness.Coordinator.WaitAsync(
                        harness.Coordinator.ExecuteAsync(request.Source.SourceIdentity,
                            () => Task.FromResult(harness.CreateFile("signed.bin", "composed"))));
                    return new ClickOncePayloadSigningResult(result);
                });
            harness.Pipeline = harness.CreatePipeline(harness.Reader, payloadHandler: handler);
            FileInfo output = new(Path.Combine(harness.Source.FullName, "out", app.Name));

            await harness.Pipeline.ExecuteAsync(new SigningOperationPlan(input, output),
                harness.Options(), harness.Coordinator, ClickOnceApplicationSigningPolicy.SignPayloads);

            Assert.Equal("unsigned", File.ReadAllText(payload.FullName));
            Assert.Empty(harness.PayloadCalls);
            AssertReferenceHashes(harness, output, new Dictionary<string, string> { [targetPath] = "composed" });
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData("Payload.dll", "payload.dll", false)]
        [InlineData("Payload.dll.deploy", "payload.dll", false)]
        [InlineData("Payload.deploy", "payload.deploy", false)]
        [InlineData("Payload.deploy.deploy", "payload.deploy", false)]
        [InlineData("Payload.dll", "payload.dll", true)]
        [InlineData("Payload.dll.deploy", "payload.dll", true)]
        [InlineData("Payload.deploy", "payload.deploy", true)]
        [InlineData("Payload.deploy.deploy", "payload.deploy", true)]
        public async Task ExecuteAsync_WhenUpdateOnlyHasMixedCaseCompletedPrerequisite_UsesOriginalResultAndPublishesOnlyManifest(
            string physical, string target, bool container)
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile(Path.Combine("Lib", physical), "unsigned");
            string targetPath = Path.Combine("lib", target);
            FileInfo app = harness.CreateManifest("App.manifest", targetPath);
            SigningFile input = OriginalInput(harness, app, container);
            SigningSourceIdentity identity = input.GetSibling(payload, harness.Source).SourceIdentity;
            FileInfo signed = harness.CreateFile(@"signed\primary.bin", "explicit signed");
            harness.CreateFile(@"signed\App.manifest", "wrong app");
            harness.CreateFile(@"signed\companion.bin", "not selected");
            int owners = 0;
            Task<SigningOperationResult> registered = harness.Coordinator.ExecuteArtifactAsync(identity, () =>
            {
                ++owners;
                return Task.FromResult(SigningOperationArtifact.Layout(signed, signed.Directory!));
            });
            await registered;
            FileInfo output = new(Path.Combine(harness.Source.FullName, "out", app.Name));

            await harness.Pipeline.ExecuteAsync(new SigningOperationPlan(input, output),
                harness.Options(), harness.Coordinator, ClickOnceApplicationSigningPolicy.UpdateManifestOnly,
                context: new ClickOnceApplicationSigningContext(new[] { identity }));

            Assert.Equal(1, owners);
            Assert.True(harness.Coordinator.TryGetResult(identity, out Task<SigningOperationResult>? existing));
            Assert.Same(registered, existing);
            Assert.Empty(harness.PayloadCalls);
            Assert.Single(output.Directory!.EnumerateFiles("*", SearchOption.AllDirectories));
            Assert.Equal("unsigned", File.ReadAllText(payload.FullName));
            AssertReferenceHashes(harness, output, new Dictionary<string, string> { [targetPath] = "explicit signed" });
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task ExecuteAsync_WhenMixedCaseOriginalPrerequisiteIsPending_WaitsBeforeMetadataAndPreservesFailureIdentity(int failureKind)
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile(@"Lib\Payload.dll.deploy", "unsigned");
            string target = @"lib\payload.dll";
            FileInfo app = harness.CreateManifest("App.manifest", target);
            string original = File.ReadAllText(app.FullName);
            SigningFile input = OriginalInput(harness, app, container: true);
            SigningSourceIdentity identity = input.GetSibling(payload, harness.Source).SourceIdentity;
            TaskCompletionSource<FileInfo> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<SigningOperationResult> prerequisite = harness.Coordinator.ExecuteAsync(identity, () => completion.Task);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            IApplicationManifest manifest = harness.UseFakeManifest("App.manifest", target);
            harness.AfterResolve = _ => entered.TrySetResult();
            List<string> order = new();
            manifest.When(value => value.SynchronizeAssemblyIdentities()).Do(_ =>
            {
                Assert.True(prerequisite.IsCompletedSuccessfully);
                Assert.Equal("signed", File.ReadAllText(Assert.Single(harness.FakeReferences).ResolvedPath!));
                order.Add("identity");
            });
            harness.BeforeUpdate = () => order.Add("update");
            harness.BeforeManifestSign = _ => order.Add("sign");
            ClickOnceApplicationSigningContext context = new(new[] { identity });
            FileInfo output = new(Path.Combine(harness.Source.FullName, "out", app.Name));
            Task owner = harness.Pipeline.ExecuteAsync(new SigningOperationPlan(input, output),
                harness.Options(), harness.Coordinator, ClickOnceApplicationSigningPolicy.UpdateManifestOnly,
                context: context);
            Task? waiter = null;
            Exception? failure = failureKind == 0 ? null : failureKind == 1
                ? new SigningException("original prerequisite")
                : new OperationCanceledException("original prerequisite");
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                FileInfo waiterOutput = new(Path.Combine(harness.Source.FullName, "waiter-out", app.Name));
                waiter = harness.Pipeline.ExecuteAsync(new SigningOperationPlan(input, waiterOutput),
                    harness.Options(), harness.Coordinator, ClickOnceApplicationSigningPolicy.UpdateManifestOnly,
                    context: context);
                Assert.False(owner.IsCompleted);
                Assert.Empty(order);
                if (failure is not null)
                {
                    completion.SetException(failure);
                }
            }
            finally
            {
                completion.TrySetResult(harness.CreateFile("signed.bin", "signed"));
            }

            if (failure is null)
            {
                await Task.WhenAll(prerequisite, owner, waiter!).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(new[] { "identity", "update", "sign" }, order);
                Assert.Single(output.Directory!.EnumerateFiles("*", SearchOption.AllDirectories));
            }
            else
            {
                foreach (Task task in new Task[] { prerequisite, owner, waiter! })
                {
                    Exception actual = await Assert.ThrowsAnyAsync<Exception>(
                        () => task.WaitAsync(TimeSpan.FromSeconds(5)));
                    Assert.Same(failure, actual);
                }
                Assert.Empty(order);
                Assert.Equal(original, File.ReadAllText(app.FullName));
            }
            Assert.Equal("unsigned", File.ReadAllText(payload.FullName));
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task ExecuteAsync_WhenReferencesDifferOnlyByCase_ReusesOneOriginalPayloadOwner(int mode)
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile(@"Lib\Payload.dll", "unsigned");
            string[] targets = { @"lib\payload.dll", @"Lib\Payload.dll" };
            FileInfo app = harness.CreateManifest("App.manifest", targets);
            SigningFile input = OriginalInput(harness, app, container: true);
            SigningSourceIdentity identity = input.GetSibling(payload, harness.Source).SourceIdentity;
            ClickOnceApplicationSigningPolicy policy = (ClickOnceApplicationSigningPolicy)mode;
            if (policy == ClickOnceApplicationSigningPolicy.UpdateManifestOnly)
            {
                await harness.Coordinator.ExecuteAsync(identity,
                    () => Task.FromResult(harness.CreateFile("signed.bin", "unsigned signed")));
            }
            FileInfo output = new(Path.Combine(harness.Source.FullName, "out", app.Name));

            await harness.Pipeline.ExecuteAsync(new SigningOperationPlan(input, output),
                harness.Options(), harness.Coordinator, policy,
                context: new ClickOnceApplicationSigningContext(new[] { identity }));

            Assert.Equal(mode == 0 ? 1 : 0, harness.PayloadCalls.Count);
            Assert.True(harness.Coordinator.TryGetResult(identity, out _));
            Assert.Equal(mode == 0 ? 2 : 1, output.Directory!.EnumerateFiles("*", SearchOption.AllDirectories).Count());
            AssertReferenceHashes(harness, output, targets.ToDictionary(target => target, _ => "unsigned signed"));
            Assert.Equal("unsigned", File.ReadAllText(payload.FullName));
            AssertNoStagingDirectories(harness);
        }

        [RequiresCaseSensitiveDirectoryTheory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_WhenMatcherTargetsDistinctCaseSensitiveEntry_DoesNotMergeEntries(bool excluded)
        {
            await using Harness harness = new();
            Assert.True(EnableCaseSensitivity(harness.Source));
            FileInfo upper = harness.CreateFile("Payload.dll", "upper");
            FileInfo lower = harness.CreateFile("payload.dll", "lower");
            FileInfo app = harness.CreateManifest("App.manifest", upper.Name);
            Matcher matching = new(StringComparison.Ordinal);
            matching.AddInclude(lower.Name);
            FileInfo output = new(Path.Combine(harness.Source.FullName, "out", app.Name));

            await harness.ExecuteAsync(app, output,
                options: harness.Options(excluded ? null : matching, excluded ? matching : null));

            Assert.Equal(excluded ? 1 : 0, harness.PayloadCalls.Count);
            AssertReferenceHashes(harness, output,
                new Dictionary<string, string> { [upper.Name] = excluded ? "upper signed" : "upper" });
            Assert.Equal("upper", File.ReadAllText(upper.FullName));
            Assert.Equal("lower", File.ReadAllText(lower.FullName));
        }

        [RequiresCaseSensitiveDirectoryTheory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SignOwnerAsync_WhenCaseSensitiveSourcesAreAmbiguousOrCollide_RejectsBeforeCopying(bool ambiguous)
        {
            await using Harness harness = new();
            Assert.True(EnableCaseSensitivity(harness.Source));
            FileInfo upper = harness.CreateFile("Payload.dll", "upper");
            FileInfo lower = harness.CreateFile("payload.dll", "lower");
            string[] targets = ambiguous ? new[] { "PAYLOAD.dll" } : new[] { upper.Name, lower.Name };
            FileInfo app = harness.CreateManifest("App.manifest", targets);
            using FileStream stream = app.OpenRead();
            Assert.True(harness.Reader.TryReadApplicationManifest(stream, out IApplicationManifest? manifest));
            ResolvedClickOncePayload[] payloads = manifest!.FileReferences.Cast<FileReference>()
                .Select(reference => new ResolvedClickOncePayload(
                    new FileInfo(Path.Combine(harness.Source.FullName, reference.TargetPath)), reference)).ToArray();
            ResolvedClickOncePublishLayout layout = new(null,
                new ResolvedClickOnceApplication(app, manifest, payloads),
                Array.Empty<ResolvedClickOnceAdjacentExecutable>(), Array.Empty<ClickOnceManifestDiagnostic>());
            SigningFile input = OriginalInput(harness, app, container: true);
            Assert.NotEqual(input.GetSibling(upper, harness.Source).SourceIdentity,
                input.GetSibling(lower, harness.Source).SourceIdentity);

            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() =>
                harness.Pipeline.SignOwnerAsync(input, harness.Options(), harness.Coordinator,
                    ClickOnceApplicationSigningPolicy.SignPayloads, layout));

            if (ambiguous)
            {
                Assert.IsType<ClickOncePublishLayoutResolutionException>(failure);
                Assert.IsType<FileNotFoundException>(failure.InnerException);
            }
            else
            {
                Assert.IsType<ClickOnceStagingException>(failure);
            }
            Assert.Empty(harness.PayloadCalls);
            Assert.Empty(harness.ManifestCalls);
            Assert.Equal("upper", File.ReadAllText(upper.FullName));
            Assert.Equal("lower", File.ReadAllText(lower.FullName));
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData("Payload.dll", "payload.dll", false)]
        [InlineData("Payload.dll.deploy", "payload.dll", false)]
        [InlineData("Payload.deploy", "payload.deploy", false)]
        [InlineData("Payload.deploy.deploy", "payload.deploy", false)]
        [InlineData("Payload.dll", "payload.dll", true)]
        [InlineData("Payload.dll.deploy", "payload.dll", true)]
        [InlineData("Payload.deploy", "payload.deploy", true)]
        [InlineData("Payload.deploy.deploy", "payload.deploy", true)]
        public async Task ExecuteAsync_WhenSequentialApplicationsShareMixedCasePayload_PreservesOriginalIdentitySpellingAndHashes(
            string physical, string target, bool outOfPlace)
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile(Path.Combine("Lib", physical), "unsigned");
            string firstTarget = Path.Combine("lib", target);
            string secondTarget = Path.Combine("Lib",
                physical.Length == target.Length ? physical : physical[..^".deploy".Length]);
            FileInfo first = harness.CreateManifest("First.manifest", firstTarget);
            FileInfo second = harness.CreateManifest("Second.manifest", secondTarget);
            SigningFile firstInput = OriginalInput(harness, first, container: true);
            SigningFile secondInput = OriginalInput(harness, second, container: true);
            SigningSourceIdentity originalIdentity = firstInput.GetSibling(payload, harness.Source).SourceIdentity;
            harness.FormatSigner.CanSign(Arg.Any<FileInfo>()).Returns(call =>
                ((FileInfo)call[0]).Extension is ".dll" or ".deploy");
            FileInfo firstOutput = outOfPlace
                ? new FileInfo(Path.Combine(harness.Source.FullName, "out1", first.Name)) : first;
            FileInfo secondOutput = outOfPlace
                ? new FileInfo(Path.Combine(harness.Source.FullName, "out2", second.Name)) : second;

            await harness.Pipeline.ExecuteAsync(new SigningOperationPlan(firstInput, firstOutput),
                harness.Options(), harness.Coordinator, ClickOnceApplicationSigningPolicy.SignPayloads);
            FileInfo afterFirst = Assert.Single(payload.Directory!.EnumerateFiles());
            string nameAfterFirst = afterFirst.Name;
            SigningSourceIdentity identityAfterFirst = secondInput.GetSibling(afterFirst, harness.Source).SourceIdentity;
            await harness.Pipeline.ExecuteAsync(new SigningOperationPlan(secondInput, secondOutput),
                harness.Options(), harness.Coordinator, ClickOnceApplicationSigningPolicy.SignPayloads);

            Assert.Single(harness.PayloadCalls);
            Assert.Equal(physical, nameAfterFirst);
            Assert.Equal(originalIdentity, identityAfterFirst);
            Assert.Equal(physical, Assert.Single(payload.Directory.EnumerateFiles()).Name);
            Assert.Equal(outOfPlace ? "unsigned" : "unsigned signed", File.ReadAllText(payload.FullName));
            AssertReferenceHashes(harness, firstOutput,
                new Dictionary<string, string> { [firstTarget] = "unsigned signed" });
            AssertReferenceHashes(harness, secondOutput,
                new Dictionary<string, string> { [secondTarget] = "unsigned signed" });
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_WhenSharedApplicationIsPublishedInAndOutOfPlace_UsesSourceSpellingInPlaceAndTargetSpellingOutOfPlace(
            bool outOfPlaceFirst)
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile(@"Lib\Payload.dll.deploy", "unsigned");
            string target = @"lib\payload.dll";
            FileInfo app = harness.CreateManifest("App.manifest", target);
            SigningFile input = OriginalInput(harness, app, container: true);
            FileInfo output = new(Path.Combine(harness.Source.FullName, "out", "Copy.manifest"));
            foreach (FileInfo destination in outOfPlaceFirst ? new[] { output, app } : new[] { app, output })
            {
                await harness.Pipeline.ExecuteAsync(new SigningOperationPlan(input, destination),
                    harness.Options(), harness.Coordinator, ClickOnceApplicationSigningPolicy.SignPayloads);
            }

            Assert.Single(harness.PayloadCalls);
            Assert.Single(harness.ManifestCalls);
            Assert.Equal(payload.Name, Assert.Single(payload.Directory!.EnumerateFiles()).Name);
            FileInfo published = Assert.Single(output.Directory!.EnumerateDirectories())
                .EnumerateFiles().Single();
            Assert.Equal("payload.dll.deploy", published.Name);
            Assert.Equal("lib", published.Directory!.Name);
            AssertReferenceHashes(harness, app, new Dictionary<string, string> { [target] = "unsigned signed" });
            AssertReferenceHashes(harness, output, new Dictionary<string, string> { [target] = "unsigned signed" });
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_WhenHandledPayloadAndCompanionAreSharedInPlace_PreservesOriginalSpellingAndOwner(bool mapped)
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile(Path.Combine("Lib",
                mapped ? "Payload.dll.deploy" : "Payload.dll"), "unsigned");
            FileInfo companion = harness.CreateFile("Meta.bin", "old metadata");
            FileInfo first = harness.CreateManifest("First.manifest", @"lib\payload.dll");
            FileInfo second = harness.CreateManifest("Second.manifest", @"Lib\Payload.dll");
            SigningFile firstInput = OriginalInput(harness, first, container: true);
            SigningFile secondInput = OriginalInput(harness, second, container: true);
            SigningFile companionInput = firstInput.GetSibling(companion, harness.Source);
            int owners = 0;
            IClickOncePayloadSigningHandler handler = Substitute.For<IClickOncePayloadSigningHandler>();
            handler.CanSign(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>()).Returns(true);
            handler.SignAsync(Arg.Any<ClickOncePayloadSigningRequest>(), Arg.Any<SignOptions>(),
                Arg.Any<SigningOperationCoordinator>()).Returns(async call =>
                {
                    ClickOncePayloadSigningRequest request = call.Arg<ClickOncePayloadSigningRequest>();
                    SigningOperationResult result = await harness.Coordinator.WaitAsync(
                        harness.Coordinator.ExecuteAsync(request.Source.SourceIdentity, () =>
                        {
                            ++owners;
                            return Task.FromResult(harness.CreateFile($"signed-{owners}.bin",
                                owners == 1 ? "unsigned signed" : "unsigned signed signed"));
                        }));
                    SigningOperationResult metadata = await harness.Coordinator.WaitAsync(
                        harness.Coordinator.ExecuteAsync(companionInput.SourceIdentity,
                            () => Task.FromResult(harness.CreateFile("signed-meta.bin", "metadata signed"))));
                    return new ClickOncePayloadSigningResult(result, new[]
                    {
                        new ClickOncePayloadSigningOutput(new SigningOperationPlan(companionInput,
                            new FileInfo(Path.Combine(request.StagingRoot.FullName, "meta.bin"))), metadata)
                    });
                });
            harness.Pipeline = harness.CreatePipeline(harness.Reader, payloadHandler: handler);

            await harness.Pipeline.ExecuteAsync(new SigningOperationPlan(firstInput, first),
                harness.Options(), harness.Coordinator, ClickOnceApplicationSigningPolicy.SignPayloads);
            string payloadName = Assert.Single(payload.Directory!.EnumerateFiles()).Name;
            string companionName = harness.Source.EnumerateFiles().Single(file =>
                file.Name.Equals("Meta.bin", StringComparison.OrdinalIgnoreCase)).Name;
            await harness.Pipeline.ExecuteAsync(new SigningOperationPlan(secondInput, second),
                harness.Options(), harness.Coordinator, ClickOnceApplicationSigningPolicy.SignPayloads);

            Assert.Equal(1, owners);
            Assert.Equal(payload.Name, payloadName);
            Assert.Equal(companion.Name, companionName);
            Assert.Equal("unsigned signed", File.ReadAllText(payload.FullName));
            Assert.Equal("metadata signed", File.ReadAllText(companion.FullName));
            AssertReferenceHashes(harness, first,
                new Dictionary<string, string> { [@"lib\payload.dll"] = "unsigned signed" });
            AssertReferenceHashes(harness, second,
                new Dictionary<string, string> { [@"Lib\Payload.dll"] = "unsigned signed" });
            Assert.Empty(harness.PayloadCalls);
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public async Task ExecuteAsync_WhenRenamedManifestIsPublishedBesideInput_PreservesPayloadSpelling()
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile(@"Lib\Payload.dll", "unsigned");
            FileInfo app = harness.CreateManifest("App.manifest", @"lib\payload.dll");
            FileInfo output = new(Path.Combine(harness.Source.FullName, "Renamed.manifest"));

            await harness.ExecuteAsync(app, output);

            Assert.Equal(payload.Name, Assert.Single(payload.Directory!.EnumerateFiles()).Name);
            Assert.Equal("unsigned signed", File.ReadAllText(payload.FullName));
            AssertReferenceHashes(harness, output,
                new Dictionary<string, string> { [@"lib\payload.dll"] = "unsigned signed" });
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public async Task ExecuteAsync_WhenOutputUsesCaseAliasedInputDirectory_PreservesExistingPayloadSpelling()
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile(@"Publish\Lib\Payload.dll", "unsigned");
            FileInfo app = harness.CreateManifest(@"Publish\App.manifest", @"lib\payload.dll");
            FileInfo output = new(Path.Combine(harness.Source.FullName, "publish", "Copy.manifest"));

            await harness.ExecuteAsync(app, output);

            Assert.Equal(payload.Name, Assert.Single(payload.Directory!.EnumerateFiles()).Name);
            AssertReferenceHashes(harness, output,
                new Dictionary<string, string> { [@"lib\payload.dll"] = "unsigned signed" });
            AssertNoStagingDirectories(harness);
        }

        [RequiresCaseSensitiveDirectoryTheory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_WhenPublishDirectoriesAreCaseDistinct_UsesOutOfPlaceTargetNaming(bool existingOutputRoot)
        {
            await using Harness harness = new();
            Assert.True(EnableCaseSensitivity(harness.Source));
            FileInfo payload = harness.CreateFile(@"Publish\Payload.dll", "unsigned");
            FileInfo app = harness.CreateManifest(@"Publish\App.manifest", "Payload.dll");
            DirectoryInfo outputRoot = new(Path.Combine(harness.Source.FullName, "publish"));
            if (existingOutputRoot)
            {
                outputRoot.Create();
            }
            FileInfo output = new(Path.Combine(outputRoot.FullName, "Copy.manifest"));
            SigningSourceIdentity parent = SigningSourceIdentity.Capture(harness.CreateFile("outer.zip"));
            SigningFile input = SigningFile.ContainerEntry(app, parent, "Publish/App.manifest");

            await harness.Pipeline.ExecuteAsync(new SigningOperationPlan(input, output),
                harness.Options(), harness.Coordinator, ClickOnceApplicationSigningPolicy.SignPayloads);

            Assert.Equal("unsigned", File.ReadAllText(payload.FullName));
            Assert.Equal("unsigned signed", File.ReadAllText(Path.Combine(outputRoot.FullName, "Payload.dll")));
            Assert.Equal("publish", outputRoot.Name);
            SigningSourceIdentity outputIdentity = SigningFile.ContainerEntry(
                new FileInfo(Path.Combine(outputRoot.FullName, "Payload.dll")), parent,
                "publish/Payload.dll").SourceIdentity;
            Assert.NotEqual(input.GetSibling(payload, app.Directory!).SourceIdentity, outputIdentity);
            Assert.False(harness.Coordinator.TryGetResult(outputIdentity, out _));
            AssertReferenceHashes(harness, output,
                new Dictionary<string, string> { ["Payload.dll"] = "unsigned signed" });
            AssertNoStagingDirectories(harness);
        }

        private static SigningFile OriginalInput(Harness harness, FileInfo app, bool container) =>
            container ? SigningFile.ContainerEntry(app,
                SigningSourceIdentity.ContainerEntry(
                    SigningSourceIdentity.Capture(harness.CreateFile("outer.zip")), "nested/Inner.zip"),
                $"app/{app.Name}") : SigningFile.Capture(app);

        private static void AssertReferenceHashes(Harness harness, FileInfo output,
            IReadOnlyDictionary<string, string> expected)
        {
            using FileStream stream = output.OpenRead();
            Assert.True(harness.Reader.TryReadApplicationManifest(stream, out IApplicationManifest? manifest));
            using (manifest)
            {
                FileReference[] references = manifest!.FileReferences.Cast<FileReference>().ToArray();
                Assert.Equal(expected.Count, references.Length);
                foreach (FileReference reference in references)
                {
                    Assert.True(expected.TryGetValue(reference.TargetPath, out string? content));
                    Assert.Equal(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(content!))),
                        reference.Hash);
                }
            }
        }

        private static bool EnableCaseSensitivity(DirectoryInfo directory)
        {
            ProcessStartInfo start = new("fsutil.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in new[] { "file", "setCaseSensitiveInfo", directory.FullName, "enable" })
            {
                start.ArgumentList.Add(argument);
            }
            using Process process = Process.Start(start)!;
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return false;
            }
            return process.ExitCode == 0;
        }

        private sealed class RequiresCaseSensitiveDirectoryTheoryAttribute : TheoryAttribute
        {
            private static readonly Lazy<bool> Available = new(() =>
            {
                try
                {
                    using DirectoryServiceStub directories = new();
                    DirectoryInfo directory = directories.CreateTemporaryDirectory();
                    if (!OperatingSystem.IsWindows() || !EnableCaseSensitivity(directory))
                    {
                        return false;
                    }
                    ClickOnceResolutionTestUtilities.CreateFile(directory, "Probe", "upper");
                    ClickOnceResolutionTestUtilities.CreateFile(directory, "probe", "lower");
                    return directory.EnumerateFiles().Count() == 2;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
                {
                    return false;
                }
            });

            public override string Skip
            {
                get => Available.Value ? null! : "Requires Windows case-sensitive directory support and sufficient privileges.";
                set => base.Skip = value;
            }
        }
    }
}
