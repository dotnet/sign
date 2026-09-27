// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.FileSystemGlobbing;
using NSubstitute;

namespace Sign.Core.Test
{
    public partial class AggregatingSignerTests
    {
        private static readonly SignOptions _options = new(HashAlgorithmName.SHA256, new Uri("http://timestamp.test"));

        [Fact]
        public void Constructor_WhenSignersIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSigner(
                    signers: null!,
                    Substitute.For<IDefaultDataFormatSigner>(),
                    Substitute.For<IContainerProvider>(),
                    Substitute.For<IFileMetadataService>(),
                    Substitute.For<IMatcherFactory>()));

            Assert.Equal("signers", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenDefaultSignerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSigner(
                    Enumerable.Empty<IDataFormatSigner>(),
                    defaultSigner: null!,
                    Substitute.For<IContainerProvider>(),
                    Substitute.For<IFileMetadataService>(),
                    Substitute.For<IMatcherFactory>()));

            Assert.Equal("defaultSigner", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenContainerProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSigner(
                    Enumerable.Empty<IDataFormatSigner>(),
                    Substitute.For<IDefaultDataFormatSigner>(),
                    containerProvider: null!,
                    Substitute.For<IFileMetadataService>(),
                    Substitute.For<IMatcherFactory>()));

            Assert.Equal("containerProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenFileMetadataServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSigner(
                    Enumerable.Empty<IDataFormatSigner>(),
                    Substitute.For<IDefaultDataFormatSigner>(),
                    Substitute.For<IContainerProvider>(),
                    fileMetadataService: null!,
                    Substitute.For<IMatcherFactory>()));

            Assert.Equal("fileMetadataService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenMatcherFactoryIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSigner(
                    Enumerable.Empty<IDataFormatSigner>(),
                    Substitute.For<IDefaultDataFormatSigner>(),
                    Substitute.For<IContainerProvider>(),
                    Substitute.For<IFileMetadataService>(),
                    matcherFactory: null!));

            Assert.Equal("matcherFactory", exception.ParamName);
        }

        [Fact]
        public void CanSign_WhenFileIsNull_Throws()
        {
            AggregatingSigner aggregatingSigner = CreateSigner();

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => aggregatingSigner.CanSign(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Fact]
        public void CanSign_WhenSignerReturnsTrue_ReturnsTrue()
        {
            const string extension = ".xyz";
            FileInfo file = new($"file{extension}");
            IDataFormatSigner signer = Substitute.For<IDataFormatSigner>();

            signer.CanSign(Arg.Any<FileInfo>()).Returns(true);
            AggregatingSigner aggregatingSigner = CreateSigner(signer);

            Assert.True(aggregatingSigner.CanSign(file));
            signer.Received(1).CanSign(Arg.Is<FileInfo>(f => ReferenceEquals(f, file)));
            Assert.Single(signer.ReceivedCalls());
        }

        [Fact]
        public void CanSign_WhenSignerReturnsFalse_ReturnsFalse()
        {
            const string extension = ".xyz";
            FileInfo file = new($"file{extension}");
            IDataFormatSigner signer = Substitute.For<IDataFormatSigner>();

            signer.CanSign(Arg.Any<FileInfo>()).Returns(false);
            AggregatingSigner aggregatingSigner = CreateSigner(signer);

            Assert.False(aggregatingSigner.CanSign(file));
            signer.Received(1).CanSign(Arg.Is<FileInfo>(f => ReferenceEquals(f, file)));
            Assert.Single(signer.ReceivedCalls());
        }

        [Theory]
        [InlineData(".appxupload")]
        [InlineData(".msixupload")]
        [InlineData(".zip")]
        [InlineData(".ZIP")] // test case insensitivity
        public void CanSign_WhenExtensionIsSpecialCase_ReturnsTrue(string extension)
        {
            AggregatingSigner aggregatingSigner = CreateSigner();

            Assert.True(aggregatingSigner.CanSign(new FileInfo($"file{extension}")));
        }

        [Fact]
        public async Task SignAsync_WhenFilesIsNull_Throws()
        {
            AggregatingSigner aggregatingSigner = CreateSigner();

            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => aggregatingSigner.SignAsync(
                    files: (IEnumerable<FileInfo>)null!,
                    _options));

            Assert.Equal("files", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenOptionsIsNull_Throws()
        {
            AggregatingSigner aggregatingSigner = CreateSigner();

            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => aggregatingSigner.SignAsync(Enumerable.Empty<FileInfo>(), options: null!));

            Assert.Equal("options", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenFilesIsEmpty_Returns()
        {
            AggregatingSigner aggregatingSigner = CreateSigner();

            await aggregatingSigner.SignAsync(Enumerable.Empty<FileInfo>(), _options);
        }

        [Fact]
        public void HasSigningWork_WhenSignerCanSign_ReturnsTrue()
        {
            IDataFormatSigner signer = Substitute.For<IDataFormatSigner>();
            FileInfo file = new("payload.dll");

            signer.CanSign(file).Returns(true);

            Assert.True(
                CreateSigner(signer).HasSigningWork(file, _options));
        }

        [Fact]
        public void HasSigningWork_WhenPortableExecutable_ReturnsTrue()
        {
            IFileMetadataService fileMetadataService =
                Substitute.For<IFileMetadataService>();
            FileInfo file = new("payload.dll.deploy");

            fileMetadataService.IsPortableExecutable(file).Returns(true);
            AggregatingSigner aggregatingSigner = new(
                Enumerable.Empty<IDataFormatSigner>(),
                Substitute.For<IDefaultDataFormatSigner>(),
                Substitute.For<IContainerProvider>(),
                fileMetadataService,
                Substitute.For<IMatcherFactory>());

            Assert.True(
                aggregatingSigner.HasSigningWork(file, _options));
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void HasSigningWork_WhenContainer_DependsOnRecursion(
            bool recurseContainers,
            bool expected)
        {
            IContainerProvider containerProvider =
                Substitute.For<IContainerProvider>();
            FileInfo file = new("container.zip");
            SignOptions options = new(
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256,
                new Uri("http://timestamp.test"),
                matcher: null,
                antiMatcher: null,
                recurseContainers);

            containerProvider.IsZipContainer(file).Returns(true);
            AggregatingSigner aggregatingSigner = new(
                Enumerable.Empty<IDataFormatSigner>(),
                Substitute.For<IDefaultDataFormatSigner>(),
                containerProvider,
                Substitute.For<IFileMetadataService>(),
                Substitute.For<IMatcherFactory>());

            Assert.Equal(
                expected,
                aggregatingSigner.HasSigningWork(file, options));
        }

        [Fact]
        public void HasSigningWork_WhenNothingProcessesFile_ReturnsFalse()
        {
            Assert.False(
                CreateSigner().HasSigningWork(
                    new FileInfo("App.exe.manifest"),
                    _options));
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        public void IsSequentialCoordinationRequired_ReflectsMatchingSigner(
            bool canSign,
            bool requiresSequentialCoordination,
            bool expected)
        {
            IDataFormatSigner signer = Substitute.For<IDataFormatSigner>();
            FileInfo file = new("file.custom");

            signer.CanSign(file).Returns(canSign);
            signer.RequiresSequentialCoordination
                .Returns(requiresSequentialCoordination);

            Assert.Equal(
                expected,
                CreateSigner(signer).IsSequentialCoordinationRequired(file));
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        public void IsOriginalFileNameRequired_ReflectsMatchingSigner(
            bool canSign,
            bool requiresOriginalFileName,
            bool expected)
        {
            IDataFormatSigner signer = Substitute.For<IDataFormatSigner>();
            FileInfo file = new("file.custom");

            signer.CanSign(file).Returns(canSign);
            signer.RequiresOriginalFileName
                .Returns(requiresOriginalFileName);

            Assert.Equal(
                expected,
                CreateSigner(signer).IsOriginalFileNameRequired(file));
        }

        [Fact]
        public async Task SignAsync_ExplicitAndRecursivePhysicalRequests_SignOnce()
        {
            using TestDirectory directory = new();
            FileInfo original = directory.CreateFile(
                relativePath: "original.dll",
                contents: "unsigned");

            await AssertDuplicateRequestsSignOnceAsync(
                directory,
                SigningSourceIdentity.Capture(original));
        }

        [Fact]
        public async Task SignAsync_DuplicateNestedContainerRequests_SignOnce()
        {
            using TestDirectory directory = new();
            SigningSourceIdentity identity =
                SigningSourceIdentity.ContainerEntry(
                    SigningSourceIdentity.PhysicalFile(
                        Path.Combine(
                            directory.FullPath,
                            "container.zip")),
                    "nested/payload.dll");

            await AssertDuplicateRequestsSignOnceAsync(
                directory,
                identity);
        }

        [Fact]
        public async Task SignAsync_DuplicateContainers_OpenSaveAndSignContentsOnce()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService);
            FileInfo firstContainer = directory.CreateFile(
                relativePath: Path.Combine(
                    "first",
                    "container.zip"),
                contents: "container");
            FileInfo secondContainer = directory.CreateFile(
                relativePath: Path.Combine(
                    "second",
                    "container.zip"),
                contents: "container");
            FileInfo firstPayload = directory.CreateFile(
                relativePath: Path.Combine(
                    "first",
                    "payload.dll"),
                contents: "unsigned");
            FileInfo secondPayload = directory.CreateFile(
                relativePath: Path.Combine(
                    "second",
                    "payload.dll"),
                contents: "unsigned");
            SigningSourceIdentity containerIdentity =
                SigningSourceIdentity.PhysicalFile(
                    Path.Combine(
                        directory.FullPath,
                        "original",
                        "container.zip"));
            SigningSourceIdentity payloadIdentity =
                SigningSourceIdentity.ContainerEntry(
                    containerIdentity,
                    "payload.dll");
            IContainer first = Substitute.For<IContainer>();
            IContainer second = Substitute.For<IContainer>();
            IContainerProvider containerProvider =
                Substitute.For<IContainerProvider>();
            IDataFormatSigner signer =
                Substitute.For<IDataFormatSigner>();
            int signingCount = 0;

            first.GetFiles(containerIdentity).Returns(
                new[]
                {
                    new SigningFile(firstPayload, payloadIdentity)
                });
            second.GetFiles(containerIdentity).Returns(
                new[]
                {
                    new SigningFile(secondPayload, payloadIdentity)
                });
            first
                .SaveAsync()
                .Returns(
                    _ =>
                    {
                        File.AppendAllText(
                            firstContainer.FullName,
                            "-saved");

                        return ValueTask.CompletedTask;
                    });
            second
                .SaveAsync()
                .Returns(
                    _ =>
                    {
                        File.AppendAllText(
                            secondContainer.FullName,
                            "-saved");

                        return ValueTask.CompletedTask;
                    });
            containerProvider
                .IsZipContainer(Arg.Any<FileInfo>())
                .Returns(
                    call =>
                        call.Arg<FileInfo>().Extension.Equals(
                            ".zip",
                            StringComparison.OrdinalIgnoreCase));
            containerProvider
                .GetContainer(Arg.Any<FileInfo>())
                .Returns(
                    call =>
                        FileInfoComparer.Instance.Equals(
                            call.Arg<FileInfo>(),
                            firstContainer)
                                ? first
                                : second);
            signer
                .CanSign(Arg.Any<FileInfo>())
                .Returns(
                    call =>
                        call.Arg<FileInfo>().Extension.Equals(
                            ".dll",
                            StringComparison.OrdinalIgnoreCase));
            signer
                .SignAsync(
                    Arg.Any<IEnumerable<FileInfo>>(),
                    _options)
                .Returns(
                    call =>
                    {
                        foreach (FileInfo payload in
                            call.Arg<IEnumerable<FileInfo>>())
                        {
                            Interlocked.Increment(ref signingCount);
                            File.AppendAllText(
                                payload.FullName,
                                "-signed");
                        }

                        return Task.CompletedTask;
                    });
            AggregatingSigner aggregatingSigner = new(
                new[] { signer },
                Substitute.For<IDefaultDataFormatSigner>(),
                containerProvider,
                Substitute.For<IFileMetadataService>(),
                Substitute.For<IMatcherFactory>());

            await aggregatingSigner.SignAsync(
                new[]
                {
                    new SigningFile(
                        firstContainer,
                        containerIdentity),
                    new SigningFile(
                        secondContainer,
                        containerIdentity)
                },
                _options,
                coordinator);

            Assert.Equal(expected: 1, actual: signingCount);
            await first.Received(1).OpenAsync();
            await first.Received(1).SaveAsync();
            first.Received(1).Dispose();
            await second.DidNotReceive().OpenAsync();
            await second.DidNotReceive().SaveAsync();
            second.DidNotReceive().Dispose();
            Assert.Equal(
                "container-saved",
                File.ReadAllText(firstContainer.FullName));
            Assert.Equal(
                "container-saved",
                File.ReadAllText(secondContainer.FullName));
        }

        [Fact]
        public async Task SignAsync_ClickOnceLayoutInContainer_SignsPayloadBeforeDeploymentManifest()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService);
            FileInfo zipFile = directory.CreateFile(
                relativePath: "container.zip",
                contents: "container");
            FileInfo application = directory.CreateFile(
                relativePath: Path.Combine(
                    "extracted",
                    "App.application"),
                contents: "application");
            FileInfo payload = directory.CreateFile(
                relativePath: Path.Combine(
                    "extracted",
                    "Payload.dll"),
                contents: "unsigned");
            SigningSourceIdentity zipIdentity =
                SigningSourceIdentity.Capture(zipFile);
            IContainer container = Substitute.For<IContainer>();
            IContainerProvider containerProvider =
                Substitute.For<IContainerProvider>();
            IDataFormatSigner deploymentManifestSigner =
                Substitute.For<IDataFormatSigner>();
            IDataFormatSigner payloadSigner =
                Substitute.For<IDataFormatSigner>();
            string? payloadContentsWhenManifestSigned = null;

            container
                .GetFiles(zipIdentity)
                .Returns(
                    new[]
                    {
                        new SigningFile(
                            application,
                            SigningSourceIdentity.ContainerEntry(
                                zipIdentity,
                                "App.application")),
                        new SigningFile(
                            payload,
                            SigningSourceIdentity.ContainerEntry(
                                zipIdentity,
                                "Payload.dll"))
                    });
            containerProvider
                .IsZipContainer(zipFile)
                .Returns(true);
            containerProvider
                .GetContainer(zipFile)
                .Returns(container);
            deploymentManifestSigner
                .RequiresSequentialCoordination
                .Returns(true);
            deploymentManifestSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(
                    call =>
                        call.Arg<FileInfo>().Extension.Equals(
                            ".application",
                            StringComparison.OrdinalIgnoreCase));
            deploymentManifestSigner
                .SignAsync(
                    Arg.Any<SigningFile>(),
                    _options,
                    coordinator)
                .Returns(
                    _ =>
                    {
                        // Manifest signing hashes the current payload bytes.
                        payloadContentsWhenManifestSigned =
                            File.ReadAllText(payload.FullName);

                        return Task.CompletedTask;
                    });
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
                    _options)
                .Returns(
                    call =>
                    {
                        foreach (FileInfo file in
                            call.Arg<IEnumerable<FileInfo>>())
                        {
                            File.AppendAllText(file.FullName, "-signed");
                        }

                        return Task.CompletedTask;
                    });
            AggregatingSigner aggregatingSigner = new(
                new[] { deploymentManifestSigner, payloadSigner },
                Substitute.For<IDefaultDataFormatSigner>(),
                containerProvider,
                Substitute.For<IFileMetadataService>(),
                Substitute.For<IMatcherFactory>());

            await aggregatingSigner.SignAsync(
                new[] { new SigningFile(zipFile, zipIdentity) },
                _options,
                coordinator);

            Assert.Equal(
                "unsigned-signed",
                payloadContentsWhenManifestSigned);
        }

        [Fact]
        public async Task SignAsync_CoordinatedOrdinaryFiles_SignsOneBatchPerSigner()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService);
            FileInfo[] files = new[]
            {
                directory.CreateFile("a.dll", "unsigned"),
                directory.CreateFile("b.dll", "unsigned"),
                directory.CreateFile("c.bin", "unsigned"),
                directory.CreateFile("d.bin", "unsigned")
            };
            IDataFormatSigner signer = CreateBatchSigner(".dll");
            IDataFormatSigner defaultSigner = CreateBatchSigner(".bin");
            IDefaultDataFormatSigner defaultDataFormatSigner =
                Substitute.For<IDefaultDataFormatSigner>();
            IFileMetadataService fileMetadataService =
                Substitute.For<IFileMetadataService>();
            ConcurrentQueue<string[]> signerBatches = new();
            ConcurrentQueue<string[]> defaultSignerBatches = new();

            RecordBatches(signer, signerBatches);
            RecordBatches(defaultSigner, defaultSignerBatches);
            defaultDataFormatSigner.Signer.Returns(defaultSigner);
            fileMetadataService
                .IsPortableExecutable(Arg.Any<FileInfo>())
                .Returns(true);
            AggregatingSigner aggregatingSigner = new(
                new[] { signer },
                defaultDataFormatSigner,
                Substitute.For<IContainerProvider>(),
                fileMetadataService,
                Substitute.For<IMatcherFactory>());

            await aggregatingSigner.SignAsync(
                files.Select(SigningFile.Capture),
                _options,
                coordinator);

            Assert.Equal(
                new[] { "a.dll", "b.dll" },
                Assert.Single(signerBatches));
            Assert.Equal(
                new[] { "c.bin", "d.bin" },
                Assert.Single(defaultSignerBatches));
            Assert.All(
                files,
                file => Assert.Equal(
                    "unsigned-signed",
                    File.ReadAllText(file.FullName)));
        }

        [Fact]
        public async Task SignAsync_CoordinatedFileOwnedElsewhere_IsExcludedFromBatchAndMaterialized()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService);
            FileInfo sharedOwnerFile = directory.CreateFile(
                Path.Combine("owner", "shared.dll"),
                "unsigned");
            FileInfo shared = directory.CreateFile(
                Path.Combine("waiter", "shared.dll"),
                "unsigned");
            FileInfo owned = directory.CreateFile(
                Path.Combine("waiter", "owned.dll"),
                "unsigned");
            SigningSourceIdentity sharedIdentity =
                SigningSourceIdentity.Capture(sharedOwnerFile);
            TaskCompletionSource releaseOwner = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource batchSigned = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            IDataFormatSigner signer = CreateBatchSigner(".dll");
            ConcurrentQueue<string[]> batches = new();

            RecordBatches(signer, batches, batchSigned);
            Task<SigningOperationResult> ownerResult =
                coordinator.ExecuteArtifactAsync(
                    sharedIdentity,
                    async () =>
                    {
                        await releaseOwner.Task;
                        File.AppendAllText(
                            sharedOwnerFile.FullName,
                            "-signed-by-owner");

                        return SigningOperationArtifact.Single(
                            sharedOwnerFile);
                    });
            AggregatingSigner aggregatingSigner = CreateSigner(signer);

            Task signTask = aggregatingSigner.SignAsync(
                new[]
                {
                    new SigningFile(shared, sharedIdentity),
                    SigningFile.Capture(owned)
                },
                _options,
                coordinator);

            try
            {
                await batchSigned.Task.WaitAsync(TimeSpan.FromSeconds(10));

                Assert.Equal(
                    new[] { "owned.dll" },
                    Assert.Single(batches));
                Assert.False(signTask.IsCompleted);
            }
            finally
            {
                // Coordinator disposal waits for the owner.
                releaseOwner.TrySetResult();
            }

            await signTask.WaitAsync(TimeSpan.FromSeconds(10));
            await ownerResult;

            Assert.Equal(
                "unsigned-signed-by-owner",
                File.ReadAllText(shared.FullName));
            Assert.Equal(
                "unsigned-signed",
                File.ReadAllText(owned.FullName));
        }

        [Fact]
        public async Task SignAsync_CoordinatedBatchFails_FaultsEveryOwnedFile()
        {
            using TestDirectory directory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService);
            FileInfo first = directory.CreateFile("a.dll", "unsigned");
            FileInfo second = directory.CreateFile("b.dll", "unsigned");
            IDataFormatSigner signer = CreateBatchSigner(".dll");
            InvalidOperationException failure = new("batch failed");
            bool laterOperationInvoked = false;

            signer
                .SignAsync(
                    Arg.Any<IEnumerable<FileInfo>>(),
                    Arg.Any<SignOptions>())
                .Returns(Task.FromException(failure));
            AggregatingSigner aggregatingSigner = CreateSigner(signer);

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => aggregatingSigner.SignAsync(
                        new[]
                        {
                            SigningFile.Capture(first),
                            SigningFile.Capture(second)
                        },
                        _options,
                        coordinator));

            Assert.Same(failure, exception);

            foreach (FileInfo file in new[] { first, second })
            {
                InvalidOperationException ownedException =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => coordinator.ExecuteArtifactAsync(
                            SigningSourceIdentity.Capture(file),
                            () =>
                            {
                                laterOperationInvoked = true;

                                return Task.FromResult(
                                    SigningOperationArtifact.Single(file));
                            }));

                Assert.Same(failure, ownedException);
            }

            Assert.False(laterOperationInvoked);
        }

        private static IDataFormatSigner CreateBatchSigner(string extension)
        {
            IDataFormatSigner signer = Substitute.For<IDataFormatSigner>();

            signer
                .CanSign(Arg.Any<FileInfo>())
                .Returns(
                    call => call.Arg<FileInfo>().Extension.Equals(
                        extension,
                        StringComparison.OrdinalIgnoreCase));

            return signer;
        }

        private static void RecordBatches(
            IDataFormatSigner signer,
            ConcurrentQueue<string[]> batches,
            TaskCompletionSource? batchSigned = null)
        {
            signer
                .SignAsync(
                    Arg.Any<IEnumerable<FileInfo>>(),
                    Arg.Any<SignOptions>())
                .Returns(
                    call =>
                    {
                        List<FileInfo> files = call
                            .Arg<IEnumerable<FileInfo>>()
                            .ToList();

                        foreach (FileInfo file in files)
                        {
                            File.AppendAllText(file.FullName, "-signed");
                        }

                        batches.Enqueue(
                            files
                                .Select(file => file.Name)
                                .Order(StringComparer.Ordinal)
                                .ToArray());
                        batchSigned?.TrySetResult();

                        return Task.CompletedTask;
                    });
        }

        private static async Task AssertDuplicateRequestsSignOnceAsync(
            TestDirectory directory,
            SigningSourceIdentity identity)
        {
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService);
            FileInfo first = directory.CreateFile(
                relativePath: Path.Combine("first", "payload.dll"),
                contents: "unsigned");
            FileInfo second = directory.CreateFile(
                relativePath: Path.Combine("second", "payload.dll"),
                contents: "unsigned");
            IDataFormatSigner signer =
                Substitute.For<IDataFormatSigner>();
            int signingCount = 0;

            signer.CanSign(Arg.Any<FileInfo>()).Returns(true);
            signer
                .SignAsync(
                    Arg.Any<IEnumerable<FileInfo>>(),
                    _options)
                .Returns(
                    call =>
                    {
                        foreach (FileInfo file in
                            call.Arg<IEnumerable<FileInfo>>())
                        {
                            Interlocked.Increment(ref signingCount);
                            File.AppendAllText(
                                file.FullName,
                                "-signed");
                        }

                        return Task.CompletedTask;
                    });
            AggregatingSigner aggregatingSigner =
                CreateSigner(signer);

            await aggregatingSigner.SignAsync(
                new[]
                {
                    new SigningFile(first, identity),
                    new SigningFile(second, identity)
                },
                _options,
                coordinator);

            Assert.Equal(expected: 1, actual: signingCount);
            Assert.Equal(
                "unsigned-signed",
                File.ReadAllText(first.FullName));
            Assert.Equal(
                "unsigned-signed",
                File.ReadAllText(second.FullName));
        }

        private static AggregatingSigner CreateSigner(IDataFormatSigner? signer = null)
        {
            IEnumerable<IDataFormatSigner> signers;

            if (signer is null)
            {
                signers = Enumerable.Empty<IDataFormatSigner>();
            }
            else
            {
                signers = [signer];
            }

            IMatcherFactory matcherFactory = Substitute.For<IMatcherFactory>();
            matcherFactory.Create().Returns(new Matcher(StringComparison.OrdinalIgnoreCase));

            return new AggregatingSigner(
                signers,
                Substitute.For<IDefaultDataFormatSigner>(),
                Substitute.For<IContainerProvider>(),
                Substitute.For<IFileMetadataService>(),
                matcherFactory);
        }
    }
}