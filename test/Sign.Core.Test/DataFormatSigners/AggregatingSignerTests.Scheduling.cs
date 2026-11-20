// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public partial class AggregatingSignerTests
    {
        [Theory]
        [InlineData(1, false, false)]
        [InlineData(1, false, true)]
        [InlineData(1, true, false)]
        [InlineData(1, true, true)]
        [InlineData(4, false, false)]
        [InlineData(4, false, true)]
        [InlineData(4, true, false)]
        [InlineData(4, true, true)]
        public async Task Scheduling_ContainersSharingPrerequisite_ClaimOnceAndMaterializeNamedResults(
            int maxConcurrency, bool mixedContainers, bool reverse)
        {
            await SchedulingContainersAsync(maxConcurrency, mixedContainers, reverse, fail: false);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(4, false)]
        [InlineData(4, true)]
        public async Task Scheduling_ContainerPrerequisiteFault_DrainsOwnersAndReleasesPermits(
            int maxConcurrency, bool mixedContainers)
        {
            await SchedulingContainersAsync(maxConcurrency, mixedContainers, reverse: true, fail: true);
        }

        private static async Task SchedulingContainersAsync(
            int maxConcurrency, bool mixedContainers, bool reverse, bool fail)
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            FileInfo root = directory.CreateFile("root.zip", "root");
            SigningSourceIdentity rootIdentity = SigningSourceIdentity.Capture(root);
            SigningOperationScope scope = SigningOperationScope.Create(
                new SigningOperationPlan(SigningFile.Capture(root), root), _ => false);
            await using SigningOperationCoordinator coordinator = new(directoryService, scope);
            using IDisposable scheduling = coordinator.UseScheduling(maxConcurrency);
            IContainerProvider provider = Substitute.For<IContainerProvider>();
            IDataFormatSigner payloadSigner = CreateBatchSigner(".dll");
            InvalidOperationException failure = new("shared prerequisite failed");
            int payloadSignCount = 0;
            payloadSigner.SignAsync(Arg.Any<IEnumerable<FileInfo>>(), _options)
                .Returns(async call =>
                {
                    Interlocked.Increment(ref payloadSignCount);
                    await Task.Delay(20);
                    if (fail)
                    {
                        throw failure;
                    }
                    FileInfo payload = Assert.Single(call.Arg<IEnumerable<FileInfo>>());
                    File.WriteAllText(payload.FullName, "shared-signed");
                });
            AggregatingSigner signer = new(
                new[] { payloadSigner }, Substitute.For<IDefaultDataFormatSigner>(),
                provider, Substitute.For<IFileMetadataService>(), new MatcherFactory());
            SigningSourceIdentity sharedIdentity =
                SigningSourceIdentity.ContainerEntry(rootIdentity, "shared.dll");
            List<SigningFile> requests = new();
            List<(IContainer Container, FileInfo Source, FileInfo Destination, FileInfo Payload)> containers = new();
            for (int index = 0; index < 2; ++index)
            {
                string name = index == 1 && mixedContainers ? "second.appx" : $"container{index}.zip";
                FileInfo source = directory.CreateFile(Path.Combine("source", name), $"unsigned-{index}");
                FileInfo destination = directory.CreateFile(Path.Combine("destination", name), "not-materialized");
                FileInfo payload = directory.CreateFile(Path.Combine($"extracted{index}", "shared.dll"), "unsigned");
                SigningSourceIdentity identity = SigningSourceIdentity.ContainerEntry(rootIdentity, name);
                IContainer container = Substitute.For<IContainer>();
                container.GetFiles(identity).Returns(new[] { new SigningFile(payload, sharedIdentity) });
                container.SaveAsync().Returns(_ =>
                {
                    File.WriteAllText(source.FullName, $"{name}:{File.ReadAllText(payload.FullName)}");
                    return ValueTask.CompletedTask;
                });
                provider.GetContainer(source).Returns(container);
                provider.GetContainer(destination).Returns(container);
                foreach (FileInfo file in new[] { source, destination })
                {
                    provider.IsZipContainer(file).Returns(file.Extension == ".zip");
                    provider.IsAppxContainer(file).Returns(file.Extension == ".appx");
                }
                requests.Add(new SigningFile(source, identity));
                containers.Add((container, source, destination, payload));
            }
            if (reverse)
            {
                requests.Reverse();
            }
            foreach ((IContainer container, FileInfo source, FileInfo destination, FileInfo payload) in containers)
            {
                requests.Add(new SigningFile(destination, requests.Single(file =>
                    FileInfoComparer.Instance.Equals(file.File, source)).SourceIdentity));
            }

            Task<SigningOperationResult> operation = coordinator.ExecuteArtifactAsync(rootIdentity, async () =>
            {
                await signer.SignAsync(requests, _options, coordinator);
                return SigningOperationArtifact.Single(root);
            });
            if (fail)
            {
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
                    () => operation.WaitAsync(TimeSpan.FromSeconds(10))));
            }
            else
            {
                await operation.WaitAsync(TimeSpan.FromSeconds(10));
            }
            Assert.Equal(1, payloadSignCount);
            foreach ((IContainer container, FileInfo source, FileInfo destination, FileInfo payload) in containers)
            {
                await container.Received(1).OpenAsync();
                container.Received(1).Dispose();
                await container.Received(fail ? 0 : 1).SaveAsync();
                if (!fail)
                {
                    Assert.Equal($"{source.Name}:shared-signed", File.ReadAllText(source.FullName));
                    Assert.Equal(File.ReadAllBytes(source.FullName), File.ReadAllBytes(destination.FullName));
                    Assert.Equal("shared-signed", File.ReadAllText(payload.FullName));
                }
            }
            foreach (SigningSourceIdentity identity in requests.Select(file => file.SourceIdentity).Append(sharedIdentity).Distinct())
            {
                Assert.True(coordinator.TryGetResult(identity, out Task<SigningOperationResult>? result));
                Assert.True(result!.IsCompleted);
                bool invokedAgain = false;
                Task<SigningOperationResult> duplicate = coordinator.ExecuteArtifactAsync(identity, () =>
                {
                    invokedAgain = true;
                    return Task.FromResult(SigningOperationArtifact.Single(root));
                });
                if (fail)
                {
                    Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => duplicate));
                }
                else
                {
                    await duplicate;
                }
                Assert.False(invokedAgain);
            }
            await coordinator.ExecuteArtifactAsync(
                SigningSourceIdentity.ContainerEntry(rootIdentity, "after.dll"),
                () => Task.FromResult(SigningOperationArtifact.Single(root)))
                .WaitAsync(TimeSpan.FromSeconds(10));
            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            foreach (DirectoryInfo snapshotDirectory in directoryService.Directories)
            {
                Assert.False(Directory.Exists(snapshotDirectory.FullName));
            }
        }

        [Theory]
        [InlineData(1, 0, false, false)]
        [InlineData(4, 0, false, false)]
        [InlineData(1, 1, false, false)]
        [InlineData(4, 1, false, false)]
        [InlineData(1, 2, false, false)]
        [InlineData(4, 2, false, false)]
        [InlineData(1, 0, true, false)]
        [InlineData(4, 0, true, false)]
        [InlineData(1, 1, true, false)]
        [InlineData(4, 1, true, false)]
        [InlineData(1, 2, true, false)]
        [InlineData(4, 2, true, false)]
        [InlineData(1, 0, false, true)]
        [InlineData(4, 0, false, true)]
        [InlineData(1, 0, true, true)]
        [InlineData(4, 0, true, true)]
        public async Task Scheduling_RecursiveZipWithLegacyClickOnceDiscoveredDeployments_DrainsLayoutMilestones(
            int maxConcurrency, int failureMode, bool nested, bool resultFirst)
        {
            bool fail = failureMode != 0;
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            FileInfo zip = directory.CreateFile("root.zip", "unsigned zip");
            FileInfo root = nested ? directory.CreateFile("outer.zip", "unsigned outer") : zip;
            SigningSourceIdentity rootIdentity = SigningSourceIdentity.Capture(root);
            SigningSourceIdentity zipIdentity = nested
                ? SigningSourceIdentity.ContainerEntry(rootIdentity, "nested.zip")
                : rootIdentity;
            SigningOperationScope scope = SigningOperationScope.Create(
                new SigningOperationPlan(SigningFile.Capture(root), root), _ => false);
            await using SigningOperationCoordinator coordinator = new(directoryService, scope);
            using IDisposable scheduling = coordinator.UseScheduling(maxConcurrency);
            FileInfo deployment = directory.CreateFile(Path.Combine("publish", "Root.application"), "root");
            FileInfo firstDiscovered = directory.CreateFile(Path.Combine("publish", "First.application"), "first");
            FileInfo secondDiscovered = directory.CreateFile(Path.Combine("publish", "Second.application"), "second");
            FileInfo manifest = directory.CreateFile(Path.Combine("publish", "App.exe.manifest"), "manifest");
            FileInfo payload = directory.CreateFile(Path.Combine("publish", "payload.dll.deploy"), "payload");
            SigningFile[] entries = new[] { deployment, firstDiscovered, secondDiscovered, manifest, payload }
                .Select(file => SigningFile.ContainerEntry(file, zipIdentity, Path.Combine("publish", file.Name)))
                .ToArray();
            ConcurrentQueue<string> mageCalls = new();
            InvalidOperationException failure = new("delayed mage failed");
            IMageCli mage = Substitute.For<IMageCli>();
            mage.RunAsync(Arg.Any<string>()).Returns(async call =>
            {
                string arguments = call.Arg<string>();
                mageCalls.Enqueue(arguments);
                await Task.Delay(20);
                Assert.True(File.Exists(Path.Combine(payload.DirectoryName!, "payload.dll")));
                Assert.False(File.Exists(payload.FullName));
                if ((failureMode == 1 && arguments.Contains(firstDiscovered.Name, StringComparison.Ordinal)) ||
                    (failureMode == 2 && arguments.Contains(deployment.Name, StringComparison.Ordinal)))
                {
                    throw failure;
                }
                return 0;
            });
            using X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate();
            using RSA privateKey = certificate.GetRSAPrivateKey()!;
            ICertificateProvider certificateProvider = Substitute.For<ICertificateProvider>();
            certificateProvider.GetCertificateAsync(Arg.Any<CancellationToken>()).Returns(certificate);
            ISignatureAlgorithmProvider signatureProvider = Substitute.For<ISignatureAlgorithmProvider>();
            signatureProvider.GetRsaAsync(Arg.Any<CancellationToken>()).Returns(privateKey);
            IManifestSigner manifestSigner = Substitute.For<IManifestSigner>();
            manifestSigner.When(signer => signer.Sign(
                Arg.Any<FileInfo>(), Arg.Any<X509Certificate2>(), Arg.Any<RSA>(), Arg.Any<SignOptions>()))
                .Do(call => File.AppendAllText(call.Arg<FileInfo>().FullName, "-signed"));
            IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
            ClickOnceSigner clickOnceSigner = new(
                signatureProvider, certificateProvider, serviceProvider, mage, manifestSigner,
                Substitute.For<ILogger<IDataFormatSigner>>(), new FileMatcher());
            IDataFormatSigner payloadSigner = CreateBatchSigner(".dll");
            int payloadSignCount = 0;
            payloadSigner.SignAsync(Arg.Any<IEnumerable<FileInfo>>(), _options).Returns(call =>
            {
                Interlocked.Increment(ref payloadSignCount);
                FileInfo file = Assert.Single(call.Arg<IEnumerable<FileInfo>>());
                File.AppendAllText(file.FullName, "-signed");
                return Task.CompletedTask;
            });
            IContainer container = Substitute.For<IContainer>();
            container.GetFiles(zipIdentity).Returns(entries);
            container.SaveAsync().Returns(_ =>
            {
                Assert.True(File.Exists(payload.FullName));
                Assert.False(File.Exists(Path.Combine(payload.DirectoryName!, "payload.dll")));
                File.WriteAllText(zip.FullName, "signed zip");
                return ValueTask.CompletedTask;
            });
            IContainerProvider containerProvider = Substitute.For<IContainerProvider>();
            containerProvider.IsZipContainer(zip).Returns(true);
            containerProvider.GetContainer(zip).Returns(container);
            IContainer? outerContainer = null;
            if (nested)
            {
                outerContainer = Substitute.For<IContainer>();
                outerContainer.GetFiles(rootIdentity).Returns(new[] { new SigningFile(zip, zipIdentity) });
                outerContainer.SaveAsync().Returns(_ =>
                {
                    Assert.Equal("signed zip", File.ReadAllText(zip.FullName));
                    File.WriteAllText(root.FullName, "signed outer");
                    return ValueTask.CompletedTask;
                });
                containerProvider.IsZipContainer(root).Returns(true);
                containerProvider.GetContainer(root).Returns(outerContainer);
            }
            AggregatingSigner aggregatingSigner = new(
                new IDataFormatSigner[] { clickOnceSigner, payloadSigner },
                Substitute.For<IDefaultDataFormatSigner>(), containerProvider,
                Substitute.For<IFileMetadataService>(), new MatcherFactory());
            serviceProvider.GetService(typeof(IAggregatingDataFormatSigner)).Returns(aggregatingSigner);

            if (resultFirst)
            {
                FileInfo completedDeployment = directory.CreateFile(
                    Path.Combine("completed", secondDiscovered.Name), "second-signed");
                foreach (FileInfo sibling in new[] { deployment, firstDiscovered, manifest, payload })
                {
                    directory.CreateFile(Path.Combine("completed", sibling.Name),
                        $"{File.ReadAllText(sibling.FullName)}-signed");
                }
                SigningSourceIdentity identity = entries.Single(file =>
                    FileInfoComparer.Instance.Equals(file.File, secondDiscovered)).SourceIdentity;
                await coordinator.ExecuteArtifactAsync(identity, () => Task.FromResult(
                    SigningOperationArtifact.Layout(completedDeployment, completedDeployment.Directory!)))
                    .WaitAsync(TimeSpan.FromSeconds(10));
            }

            Task<SigningOperationResult> operation = coordinator.ExecuteArtifactAsync(rootIdentity, async () =>
            {
                await aggregatingSigner.SignOwnerAsync(SigningFile.Capture(root), _options, coordinator);
                return SigningOperationArtifact.Single(root);
            });
            if (fail)
            {
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
                    () => operation.WaitAsync(TimeSpan.FromSeconds(10))));
                await container.DidNotReceive().SaveAsync();
            }
            else
            {
                await operation.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(resultFirst ? 3 : 4, mageCalls.Count);
                Assert.Equal("payload-signed", File.ReadAllText(payload.FullName));
                Assert.False(File.Exists(Path.Combine(payload.DirectoryName!, "payload.dll")));
                await container.Received(1).SaveAsync();
                foreach (FileInfo discovered in new[] { firstDiscovered, secondDiscovered })
                {
                    SigningSourceIdentity identity = entries.Single(file =>
                        FileInfoComparer.Instance.Equals(file.File, discovered)).SourceIdentity;
                    Assert.True(coordinator.TryGetResult(identity, out Task<SigningOperationResult>? result));
                    Assert.True(result!.IsCompletedSuccessfully);
                    FileInfo destination = directory.CreateFile(
                        Path.Combine($"copy-{discovered.Name}", discovered.Name), "unsigned copy");
                    (await result).Materialize(destination);
                    Assert.Equal(
                        Directory.GetFiles(deployment.DirectoryName!).Select(Path.GetFileName).Order(StringComparer.Ordinal),
                        Directory.GetFiles(destination.DirectoryName!).Select(Path.GetFileName).Order(StringComparer.Ordinal));
                    foreach (FileInfo original in deployment.Directory!.EnumerateFiles())
                    {
                        Assert.Equal(File.ReadAllBytes(original.FullName),
                            File.ReadAllBytes(Path.Combine(destination.DirectoryName!, original.Name)));
                    }
                }
            }
            Assert.Equal(1, payloadSignCount);
            await container.Received(1).OpenAsync();
            container.Received(1).Dispose();
            if (outerContainer is not null)
            {
                await outerContainer.Received(1).OpenAsync();
                await outerContainer.Received(fail ? 0 : 1).SaveAsync();
                outerContainer.Received(1).Dispose();
            }
            List<Task<SigningOperationResult>> registered = new();
            foreach (SigningFile entry in entries)
            {
                if (coordinator.TryGetResult(entry.SourceIdentity, out Task<SigningOperationResult>? result))
                {
                    registered.Add(result!);
                }
            }
            Task drain = Task.WhenAll(registered);
            if (fail)
            {
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
                    () => drain.WaitAsync(TimeSpan.FromSeconds(10))));
            }
            else
            {
                await drain.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(entries.Length, registered.Count);
            }
            Assert.All(registered, result => Assert.True(result.IsCompleted));
            await coordinator.ExecuteArtifactAsync(
                SigningSourceIdentity.ContainerEntry(zipIdentity, "after.dll"),
                () => Task.FromResult(SigningOperationArtifact.Single(zip)))
                .WaitAsync(TimeSpan.FromSeconds(10));
            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            foreach (DirectoryInfo snapshotDirectory in directoryService.Directories)
            {
                Assert.False(Directory.Exists(snapshotDirectory.FullName));
            }
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(4, false)]
        [InlineData(4, true)]
        public async Task Scheduling_OwnStagingContainers_MaterializeOwnersAndBorrowedChildren(
            int maxConcurrency, bool reverse)
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            FileInfo root = directory.CreateFile("root.zip", "root");
            SigningSourceIdentity rootIdentity = SigningSourceIdentity.Capture(root);
            await using SigningOperationCoordinator coordinator = new(directoryService,
                SigningOperationScope.Create(
                    new SigningOperationPlan(SigningFile.Capture(root), root), _ => false));
            using IDisposable scheduling = coordinator.UseScheduling(maxConcurrency);
            IDataFormatSigner stagingSigner = Substitute.For<IDataFormatSigner>();
            stagingSigner.RequiresOwnStaging.Returns(true);
            stagingSigner.CanSign(Arg.Any<FileInfo>()).Returns(true);
            IContainerProvider provider = Substitute.For<IContainerProvider>();
            List<SigningFile> requests = new();
            List<IDisposable> stagingOwners = new();
            Dictionary<SigningSourceIdentity, SigningOperationArtifact> artifacts = new();
            for (int index = 0; index < 2; ++index)
            {
                string name = $"container{index}.zip";
                FileInfo source = directory.CreateFile(Path.Combine($"source{index}", name), "unsigned");
                FileInfo destination = directory.CreateFile(Path.Combine($"destination{index}", name), "unsigned");
                FileInfo staged = directory.CreateFile(Path.Combine($"staged{index}", name), $"signed-{index}");
                FileInfo stagedPayload = directory.CreateFile(Path.Combine($"staged{index}", "payload.dll"), $"payload-{index}");
                IDisposable stagingOwner = Substitute.For<IDisposable>();
                stagingOwners.Add(stagingOwner);
                SigningSourceIdentity identity = SigningSourceIdentity.ContainerEntry(rootIdentity, name);
                artifacts.Add(identity, SigningOperationArtifact.OwnedLayout(
                    staged, staged.Directory!, new[] { staged, stagedPayload }, stagingOwner));
                foreach (FileInfo file in new[] { source, destination })
                {
                    provider.IsZipContainer(file).Returns(true);
                    directory.CreateFile(Path.Combine(file.Directory!.Name, "payload.dll"), "unsigned");
                    directory.CreateFile(Path.Combine(file.Directory.Name, "excluded.dll"), "excluded");
                    requests.Add(new SigningFile(file, identity));
                }
            }
            if (reverse)
            {
                requests.Reverse();
            }
            int invocationCount = 0;
            stagingSigner.SignOwnerArtifactAsync(Arg.Any<SigningFile>(), _options, coordinator)
                .Returns(async call =>
                {
                    Interlocked.Increment(ref invocationCount);
                    await Task.Delay(20);
                    return artifacts[call.Arg<SigningFile>().SourceIdentity];
                });
            AggregatingSigner signer = new(
                new[] { stagingSigner }, Substitute.For<IDefaultDataFormatSigner>(),
                provider, Substitute.For<IFileMetadataService>(), new MatcherFactory());
            await coordinator.ExecuteArtifactAsync(rootIdentity, async () =>
            {
                await signer.SignAsync(requests, _options, coordinator);
                return SigningOperationArtifact.Single(root);
            }).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(2, invocationCount);
            foreach (SigningFile request in requests)
            {
                int index = request.File.Name == "container0.zip" ? 0 : 1;
                Assert.Equal($"signed-{index}", File.ReadAllText(request.File.FullName));
                Assert.Equal($"payload-{index}",
                    File.ReadAllText(Path.Combine(request.File.DirectoryName!, "payload.dll")));
                Assert.Equal("excluded",
                    File.ReadAllText(Path.Combine(request.File.DirectoryName!, "excluded.dll")));
            }
            foreach (IDisposable stagingOwner in stagingOwners)
            {
                stagingOwner.Received(1).Dispose();
            }
            await stagingSigner.DidNotReceive().SignAsync(Arg.Any<SigningFile>(), _options, coordinator);
            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            foreach (DirectoryInfo snapshotDirectory in directoryService.Directories)
            {
                Assert.False(Directory.Exists(snapshotDirectory.FullName));
            }
        }

        [Theory]
        [InlineData(1, false, 0)]
        [InlineData(1, true, 0)]
        [InlineData(4, false, 0)]
        [InlineData(4, true, 0)]
        [InlineData(1, false, 1)]
        [InlineData(1, true, 1)]
        [InlineData(4, false, 1)]
        [InlineData(4, true, 1)]
        [InlineData(1, false, 2)]
        [InlineData(1, true, 2)]
        [InlineData(4, false, 2)]
        [InlineData(4, true, 2)]
        public async Task Scheduling_DormantOrdinaryPhase_PublishesOnlyAfterCallbacksSettle(
            int maxConcurrency, bool inPlace, int mode)
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            string[] names = mode == 2
                ? new[] { "first.txt", "second.txt" }
                : new[] { "first.dll", "second.dll", "archive.zip", "unsupported.txt" };
            SigningOperationPlan[] plans = names.Select(name =>
            {
                FileInfo source = directory.CreateFile(Path.Combine("source", name), $"original-{name}");
                FileInfo output = inPlace ? source : new FileInfo(
                    Path.Combine(directory.FullPath, "output", name));
                return new SigningOperationPlan(SigningFile.Capture(source), output);
            }).ToArray();
            await using SigningOperationCoordinator coordinator = new(directoryService,
                SigningOperationScope.Create(plans, _ => false));
            int callbackCount = 0;
            int activeCallbacks = 0;
            InvalidOperationException failure = new("ordinary phase failed");
            void AssertUnpublished()
            {
                foreach (SigningOperationPlan plan in plans)
                {
                    if (inPlace)
                    {
                        Assert.Equal($"original-{plan.Source.File.Name}",
                            File.ReadAllText(plan.Output.FullName));
                    }
                    else
                    {
                        Assert.False(File.Exists(plan.Output.FullName));
                    }
                }
            }
            IDataFormatSigner payloadSigner = CreateBatchSigner(".dll");
            payloadSigner.SignAsync(Arg.Any<IEnumerable<FileInfo>>(), _options)
                .Returns(async call =>
                {
                    int invocation = Interlocked.Increment(ref callbackCount);
                    int active = Interlocked.Increment(ref activeCallbacks);
                    try
                    {
                        Assert.InRange(active, 1, maxConcurrency);
                        AssertUnpublished();
                        await Task.Delay(invocation == 1 ? 10 : 50);
                        AssertUnpublished();
                        if (mode == 1 && invocation == 2)
                        {
                            throw failure;
                        }
                        FileInfo file = Assert.Single(call.Arg<IEnumerable<FileInfo>>());
                        File.AppendAllText(file.FullName, "-signed");
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeCallbacks);
                    }
                });
            payloadSigner.SignAsync(Arg.Any<SigningFile>(), _options, coordinator)
                .Returns(call => payloadSigner.SignAsync(new[] { call.Arg<SigningFile>().File }, _options));
            IContainerProvider provider = Substitute.For<IContainerProvider>();
            provider.IsZipContainer(Arg.Any<FileInfo>()).Returns(call =>
                call.Arg<FileInfo>().Extension == ".zip");
            ConcurrentQueue<IContainer> openedContainers = new();
            provider.GetContainer(Arg.Any<FileInfo>()).Returns(call =>
            {
                FileInfo staged = call.Arg<FileInfo>();
                FileInfo payload = new(Path.Combine(staged.DirectoryName!, "inside.dll"));
                File.WriteAllText(payload.FullName, "inside");
                IContainer container = Substitute.For<IContainer>();
                container.GetFiles(Arg.Any<SigningSourceIdentity>()).Returns(filesCall =>
                    new[] { SigningFile.ContainerEntry(
                        payload, filesCall.Arg<SigningSourceIdentity>(), "inside.dll") });
                container.SaveAsync().Returns(_ =>
                {
                    AssertUnpublished();
                    Assert.Equal("inside-signed", File.ReadAllText(payload.FullName));
                    File.AppendAllText(staged.FullName, "-signed");
                    return ValueTask.CompletedTask;
                });
                openedContainers.Enqueue(container);
                return container;
            });
            AggregatingSigner aggregate = new(
                new[] { payloadSigner }, Substitute.For<IDefaultDataFormatSigner>(),
                provider, Substitute.For<IFileMetadataService>(), new MatcherFactory());
            IServiceProvider services = Substitute.For<IServiceProvider>();
            services.GetService(typeof(IAggregatingDataFormatSigner)).Returns(aggregate);
            services.GetService(typeof(IDirectoryService)).Returns(directoryService);
            services.GetService(typeof(ILogger<ISigner>)).Returns(Substitute.For<ILogger<ISigner>>());
            services.GetService(typeof(ILogger<IDataFormatSigner>)).Returns(Substitute.For<ILogger<IDataFormatSigner>>());
            ClickOnceDeploymentManifestSigner pipeline = new(services);

            Task operation = pipeline.ExecuteAsync(plans, _options, coordinator, maxConcurrency);
            if (mode == 1)
            {
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
                    () => operation.WaitAsync(TimeSpan.FromSeconds(10))));
                AssertUnpublished();
            }
            else
            {
                await operation.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(mode == 2 ? 0 : 3, callbackCount);
                foreach (SigningOperationPlan plan in plans)
                {
                    string original = $"original-{plan.Source.File.Name}";
                    string expected = plan.Source.File.Extension == ".txt" ? original : $"{original}-signed";
                    Assert.Equal(expected, File.ReadAllText(plan.Output.FullName));
                    if (!inPlace)
                    {
                        Assert.Equal(original, File.ReadAllText(plan.Source.File.FullName));
                    }
                }
            }
            Assert.Equal(0, activeCallbacks);
            Assert.False(coordinator.IsSchedulingEnabled);
            foreach (IContainer container in openedContainers)
            {
                await container.Received(1).OpenAsync();
                container.Received(1).Dispose();
            }
            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            foreach (DirectoryInfo snapshotDirectory in directoryService.Directories)
            {
                Assert.False(Directory.Exists(snapshotDirectory.FullName));
            }
        }
    }
}
