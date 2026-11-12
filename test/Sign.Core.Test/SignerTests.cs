// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Security.Principal;
using System.Text;
using System.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using NSubstitute;
using NuGet.Packaging;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    [Collection(SigningTestsCollection.Name)]
    public sealed class SignerTests : IDisposable
    {
        private readonly CertificatesFixture _certificatesFixture;
        private readonly DirectoryService _directoryService;
        private readonly KeyVaultServiceStub _keyVaultServiceStub;
        private readonly TemporaryDirectory _temporaryDirectory;

        public SignerTests(CertificatesFixture certificatesFixture)
        {
            ArgumentNullException.ThrowIfNull(certificatesFixture, nameof(certificatesFixture));

            _certificatesFixture = certificatesFixture;
            _keyVaultServiceStub = new KeyVaultServiceStub();
            _directoryService = new DirectoryService(Substitute.For<ILogger<IDirectoryService>>());
            _temporaryDirectory = new TemporaryDirectory(_directoryService);
        }

        public void Dispose()
        {
            _keyVaultServiceStub.Dispose();
            _temporaryDirectory.Dispose();
            _directoryService.Dispose();
        }

        [Fact]
        public void Constructor_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new Signer(serviceProvider: null!, Substitute.For<ILogger<ISigner>>()));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new Signer(Substitute.For<IServiceProvider>(), logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenPlanningFails_ReturnsFailed()
        {
            ServiceProvider serviceProvider = Create();
            TestLogger<ISigner> logger = new();
            Signer signer = new(serviceProvider, logger);

            int exitCode = await signer.SignAsync(
                inputFiles: new FileInfo[] { null! },
                outputFile: null,
                fileList: null,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 1,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Failed, exitCode);
            Assert.Contains(
                logger.Entries,
                entry => entry.LogLevel == LogLevel.Error);
        }

        [Fact]
        public async Task SignAsync_WhenSigningFails_LogsSpecificFailure()
        {
            FileInfo input = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    "input.exe"));
            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            TestLogger<ISigner> logger = new();
            SigningException expected =
                new($"Signing failed for '{input.FullName}'.");

            File.WriteAllText(input.FullName, "input");
            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(true);
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns<Task>(_ => throw expected);
            Signer signer = new(
                CreateServiceProvider(aggregatingSigner),
                logger);

            int exitCode = await signer.SignAsync(
                new[] { input },
                outputFile: null,
                fileList: null,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 1,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Failed, exitCode);
            TestLogEntry error = Assert.Single(
                logger.Entries,
                entry => entry.LogLevel == LogLevel.Error);
            Assert.Equal(expected.Message, error.Message);
        }

        [Theory]
        [InlineData(".exe", 1)]
        [InlineData(".application", 4)]
        public async Task SignAsync_InPlace_WhenLaterClassificationFails_SignsEarlierInputFirst(
            string secondExtension,
            int maxConcurrency)
        {
            FileInfo first = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    "first.exe"));
            FileInfo second = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    $"second{secondExtension}"));
            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            InvalidOperationException expected =
                new("classification failed");

            File.WriteAllText(first.FullName, "first");
            File.WriteAllText(second.FullName, "second");

            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(
                    call =>
                    {
                        FileInfo file = call.Arg<FileInfo>();

                        if (string.Equals(
                            file.FullName,
                            second.FullName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            throw expected;
                        }

                        return true;
                    });
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            StubSequentialCoordination(
                aggregatingSigner,
                ".application");
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    call =>
                    {
                        File.AppendAllText(
                            call.Arg<SigningFile>().File.FullName,
                            "-signed");

                        return Task.CompletedTask;
                    });

            int exitCode = await SignInPlaceAsync(
                aggregatingSigner,
                maxConcurrency,
                first,
                second);

            Assert.Equal(ExitCode.Failed, exitCode);
            Assert.Equal(
                "first-signed",
                File.ReadAllText(first.FullName));
        }

        [Theory]
        [InlineData(null, 1)]
        [InlineData("signed", 2)]
        public async Task SignAsync_WhenSigningInput_ClassifiesEachInputOnlyWhenRequired(
            string? outputFile,
            int expectedClassificationCount)
        {
            FileInfo input = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    "input.exe"));
            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            int classificationCount = 0;

            File.WriteAllText(input.FullName, "input");
            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(
                    _ =>
                    {
                        Interlocked.Increment(
                            ref classificationCount);

                        return true;
                    });
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(Task.CompletedTask);

            int exitCode = await SignAsync(
                aggregatingSigner,
                outputFile,
                maxConcurrency: 1,
                input);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(
                expectedClassificationCount,
                classificationCount);
        }

        [Fact]
        public async Task SignAsync_WhenInputHasNoSigningWork_DoesNotCreateTemporaryDirectory()
        {
            FileInfo input = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    "input.txt"));
            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            IDirectoryService directoryService =
                Substitute.For<IDirectoryService>();

            File.WriteAllText(input.FullName, "input");
            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(false);
            Signer signer = new(
                CreateServiceProvider(
                    aggregatingSigner,
                    directoryService),
                Substitute.For<ILogger<ISigner>>());

            int exitCode = await signer.SignAsync(
                new[] { input },
                outputFile: null,
                fileList: null,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 1,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);
            directoryService
                .DidNotReceive()
                .CreateTemporaryDirectory();
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("signed", "")]
        [InlineData(null, "nested")]
        [InlineData("signed", "nested")]
        public async Task SignAsync_ClickOnceInputsInSameOrNestedDirectory_AreSequential(
            string? outputFile,
            string secondDirectory)
        {
            FileInfo first = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    "First.application"));
            FileInfo second = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    secondDirectory,
                    "Second.application"));

            second.Directory!.Create();
            File.WriteAllText(first.FullName, "first");
            File.WriteAllText(second.FullName, "second");

            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            int activeCount = 0;
            int maximumActiveCount = 0;

            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(true);
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            StubSequentialCoordination(aggregatingSigner, ".application");
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    async call =>
                    {
                        int active =
                            Interlocked.Increment(ref activeCount);
                        int observed;

                        do
                        {
                            observed = Volatile.Read(
                                ref maximumActiveCount);
                        }
                        while (active > observed &&
                            Interlocked.CompareExchange(
                                ref maximumActiveCount,
                                active,
                                observed) != observed);

                        await Task.Delay(
                            TimeSpan.FromMilliseconds(50));
                        File.AppendAllText(
                            call.Arg<SigningFile>().File.FullName,
                            "-signed");
                        Interlocked.Decrement(ref activeCount);
                    });
            Signer signer = new(
                CreateServiceProvider(aggregatingSigner),
                Substitute.For<ILogger<ISigner>>());

            int exitCode = await signer.SignAsync(
                new[] { first, second },
                outputFile,
                fileList: null,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 2,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(expected: 1, actual: maximumActiveCount);

            string outputDirectory = outputFile is null
                ? _temporaryDirectory.Directory.FullName
                : Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    outputFile);

            Assert.Equal(
                "first-signed",
                File.ReadAllText(
                    Path.Combine(outputDirectory, first.Name)));
            Assert.Equal(
                "second-signed",
                File.ReadAllText(
                    Path.Combine(
                        outputDirectory,
                        secondDirectory,
                        second.Name)));
        }

        [Fact]
        public async Task SignAsync_ClickOnceInputsInSiblingDirectories_RunConcurrently()
        {
            // "publish2" shares a string prefix with "publish" but is not
            // nested in it.
            FileInfo first = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    "publish",
                    "App.application"));
            FileInfo second = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    "publish2",
                    "App.application"));

            first.Directory!.Create();
            second.Directory!.Create();
            File.WriteAllText(first.FullName, "first");
            File.WriteAllText(second.FullName, "second");

            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            TaskCompletionSource bothStarted = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int startedCount = 0;

            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(true);
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            StubSequentialCoordination(aggregatingSigner, ".application");
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    async _ =>
                    {
                        if (Interlocked.Increment(ref startedCount) == 2)
                        {
                            bothStarted.TrySetResult();
                        }

                        await Task.WhenAny(
                            bothStarted.Task,
                            Task.Delay(TimeSpan.FromSeconds(10)));
                    });
            Signer signer = new(
                CreateServiceProvider(aggregatingSigner),
                Substitute.For<ILogger<ISigner>>());

            int exitCode = await signer.SignAsync(
                new[] { first, second },
                outputFile: null,
                fileList: null,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 2,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.True(bothStarted.Task.IsCompletedSuccessfully);
        }

        [Theory]
        [InlineData(@"C:\App.application", @"C:\publish\App.application")]
        [InlineData(@"C:\publish\App.application", @"C:\App.application")]
        [InlineData(
            @"\\server\share\App.application",
            @"\\server\share\publish\App.application")]
        public void CreateWorkItems_WhenClickOnceInputsAreAtRootAndNested_FormsOneWorkItem(
            string firstPath,
            string secondPath)
        {
            SigningOperationPlan first = CreatePlan(firstPath);
            SigningOperationPlan second = CreatePlan(secondPath);

            IReadOnlyList<IReadOnlyList<SigningOperationPlan>> workItems =
                Signer.CreateWorkItems(
                    new[] { first, second },
                    requiresSequentialCoordination: _ => true);

            IReadOnlyList<SigningOperationPlan> workItem =
                Assert.Single(workItems);
            Assert.Equal(new[] { first, second }, workItem);

            static SigningOperationPlan CreatePlan(string path)
            {
                FileInfo file = new(path);

                return new SigningOperationPlan(
                    SigningFile.Capture(file),
                    file);
            }
        }

        [Fact]
        public void CreateWorkItems_WhenSequentialInputsUseIndependentDirectories_PerformsLinearContainmentChecks()
        {
            const int PlanCount = 10_000;
            List<SigningOperationPlan> plans = Enumerable
                .Range(0, PlanCount)
                .Select(
                    index =>
                    {
                        FileInfo file = new(
                            $@"C:\publish-{index:D5}\App.application");

                        return new SigningOperationPlan(
                            SigningFile.Capture(file),
                            file);
                    })
                .ToList();

            IReadOnlyList<IReadOnlyList<SigningOperationPlan>> workItems =
                Signer.CreateWorkItems(
                    plans,
                    requiresSequentialCoordination: _ => true,
                    out int directoryOperationCount);

            Assert.Equal(PlanCount, workItems.Count);
            Assert.Equal(PlanCount - 1, directoryOperationCount);
        }

        [Fact]
        public void CreateWorkItems_WhenSequentialInputsUseNestedDirectories_PerformsLinearContainmentChecks()
        {
            const int PlanCount = 1_000;
            string directory = @"C:\publish";
            List<SigningOperationPlan> plans = new(PlanCount);

            for (int index = 0; index < PlanCount; ++index)
            {
                FileInfo file = new(
                    Path.Combine(directory, "App.application"));
                plans.Add(
                    new SigningOperationPlan(
                        SigningFile.Capture(file),
                        file));
                directory = Path.Combine(directory, $"nested-{index:D4}");
            }

            IReadOnlyList<IReadOnlyList<SigningOperationPlan>> workItems =
                Signer.CreateWorkItems(
                    plans,
                    requiresSequentialCoordination: _ => true,
                    out int directoryOperationCount);

            IReadOnlyList<SigningOperationPlan> workItem =
                Assert.Single(workItems);
            Assert.Equal(plans, workItem);
            Assert.Equal(PlanCount - 1, directoryOperationCount);
        }

        [Fact]
        public void CreateWorkItems_DuplicateOutput_FormsOneWorkItem()
        {
            FileInfo firstSource = new(@"C:\first\App.dll");
            FileInfo secondSource = new(@"C:\second\App.dll");
            FileInfo output = new(@"C:\signed\App.dll");
            SigningOperationPlan first = new(
                SigningFile.Capture(firstSource),
                output);
            SigningOperationPlan second = new(
                SigningFile.Capture(secondSource),
                new FileInfo(output.FullName.ToUpperInvariant()));

            IReadOnlyList<IReadOnlyList<SigningOperationPlan>> workItems =
                Signer.CreateWorkItems(
                    new[] { first, second },
                    requiresSequentialCoordination: _ => false);

            IReadOnlyList<SigningOperationPlan> workItem =
                Assert.Single(workItems);
            Assert.Equal(new[] { first, second }, workItem);
        }

        [Fact]
        public async Task SignAsync_WithOutput_SignsInInputOrder()
        {
            FileInfo application = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    "App.application"));
            FileInfo payload = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    "Payload.dll"));

            File.WriteAllText(application.FullName, "application");
            File.WriteAllText(payload.FullName, "payload");

            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            List<string> signedFileExtensions = new();

            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(true);
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            StubSequentialCoordination(aggregatingSigner, ".application");
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    call =>
                    {
                        FileInfo file = call.Arg<SigningFile>().File;

                        signedFileExtensions.Add(file.Extension);
                        File.AppendAllText(file.FullName, "-signed");

                        return Task.CompletedTask;
                    });
            Signer signer = new(
                CreateServiceProvider(aggregatingSigner),
                Substitute.For<ILogger<ISigner>>());

            int exitCode = await signer.SignAsync(
                new[] { application, payload },
                outputFile: "signed",
                fileList: null,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 1,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(
                new[] { ".application", ".dll" },
                signedFileExtensions);
        }

        [Fact]
        public async Task SignAsync_NestedClickOnceInputsWithSharedDescendant_DoNotDeadlock()
        {
            // publish\App.application (parent input)
            // publish\sub\App.application (nested input)
            // publish\sub\Child.application (discovered by both)
            DirectoryInfo publish = _temporaryDirectory.Directory
                .CreateSubdirectory("publish");
            DirectoryInfo sub = publish.CreateSubdirectory("sub");
            FileInfo parent = new(
                Path.Combine(publish.FullName, "App.application"));
            FileInfo nested = new(
                Path.Combine(sub.FullName, "App.application"));
            FileInfo child = new(
                Path.Combine(sub.FullName, "Child.application"));

            File.WriteAllText(parent.FullName, "parent");
            File.WriteAllText(nested.FullName, "nested");
            File.WriteAllText(child.FullName, "child");

            using X509Certificate2 certificate =
                SelfIssuedCertificateCreator.CreateCertificate();
            IMageCli mageCli = Substitute.For<IMageCli>();
            using ManualResetEventSlim parentClaimedChild = new();
            ConcurrentQueue<string> mageArguments = new();

            mageCli
                .RunAsync(Arg.Any<string>())
                .Returns(
                    async call =>
                    {
                        string arguments = call.Arg<string>();

                        mageArguments.Enqueue(arguments);

                        // Only the parent's staging places Child.application
                        // under "sub". Hold the parent's claim on it long
                        // enough for the nested input to claim itself.
                        if (arguments.Contains(
                            $@"sub{Path.DirectorySeparatorChar}{child.Name}",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            parentClaimedChild.Set();
                            await Task.Delay(
                                TimeSpan.FromMilliseconds(500));
                        }

                        return 0;
                    });

            AggregatingSigner aggregatingSigner = CreateClickOnceSigner(
                certificate,
                mageCli);

            // Hold the nested input back until the parent owns
            // Child.application.
            ILogger<ISigner> logger = Substitute.For<ILogger<ISigner>>();

            logger
                .When(
                    value => value.Log(
                        LogLevel.Information,
                        Arg.Any<EventId>(),
                        Arg.Any<Arg.AnyType>(),
                        Arg.Any<Exception?>(),
                        Arg.Any<Func<Arg.AnyType, Exception?, string>>()))
                .Do(
                    call =>
                    {
                        string? message = call.Args()[2]?.ToString();

                        if (message is not null &&
                            message.Contains(
                                nested.FullName,
                                StringComparison.OrdinalIgnoreCase) &&
                            !message.Contains(
                                child.Name,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            parentClaimedChild.Wait(
                                TimeSpan.FromSeconds(10));
                        }
                    });

            Signer signer = new(
                CreateServiceProvider(aggregatingSigner),
                logger);
            Task<int> signTask = signer.SignAsync(
                new[] { parent, nested },
                outputFile: null,
                fileList: null,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 2,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Task completed = await Task.WhenAny(
                signTask,
                Task.Delay(TimeSpan.FromSeconds(30)));

            Assert.Same(signTask, completed);
            Assert.Equal(ExitCode.Success, await signTask);
            Assert.Equal(expected: 3, actual: mageArguments.Count);
        }

        [Fact]
        public async Task SignAsync_ClickOnceAndSiblingInputs_StagesSignedSiblingAtAnyConcurrency()
        {
            FileInfo application = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    "App.application"));
            FileInfo payload = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    "Payload.dll"));

            File.WriteAllText(application.FullName, "application");
            File.WriteAllText(payload.FullName, "payload");

            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            string? stagedPayloadContents = null;

            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(true);
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            StubSequentialCoordination(aggregatingSigner, ".application");
            aggregatingSigner
                .When(
                    value => value.StageSigningDependencies(
                        Arg.Is<FileInfo>(
                            file => file.Extension == ".application"),
                        Arg.Any<DirectoryInfo>(),
                        Arg.Any<SignOptions>()))
                .Do(
                    call =>
                    {
                        // Mirrors ClickOnce v1 dependency staging.
                        FileInfo source = call.Arg<FileInfo>();
                        DirectoryInfo staging =
                            call.Arg<DirectoryInfo>();

                        File.Copy(
                            Path.Combine(
                                source.DirectoryName!,
                                payload.Name),
                            Path.Combine(
                                staging.FullName,
                                payload.Name));
                    });
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    async call =>
                    {
                        FileInfo file = call.Arg<SigningFile>().File;

                        if (file.Extension == ".application")
                        {
                            stagedPayloadContents = File.ReadAllText(
                                Path.Combine(
                                    file.DirectoryName!,
                                    payload.Name));
                        }
                        else
                        {
                            await Task.Delay(
                                TimeSpan.FromMilliseconds(200));
                        }

                        File.AppendAllText(file.FullName, "-signed");
                    });
            Signer signer = new(
                CreateServiceProvider(aggregatingSigner),
                Substitute.For<ILogger<ISigner>>());

            int exitCode = await signer.SignAsync(
                new[] { application, payload },
                outputFile: null,
                fileList: null,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 2,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal("payload-signed", stagedPayloadContents);
            Assert.Equal(
                "payload-signed",
                File.ReadAllText(payload.FullName));
            Assert.Equal(
                "application-signed",
                File.ReadAllText(application.FullName));
        }

        [Fact]
        public async Task SignAsync_InputsRequiringSequentialCoordination_AreSequential()
        {
            FileInfo first = CreateInput("First.custom");
            FileInfo second = CreateInput("Second.custom");
            IAggregatingDataFormatSigner aggregatingSigner =
                CreateActivityTrackingSigner(
                    overlapWindow: TimeSpan.FromMilliseconds(250),
                    out Func<int> getMaximumActiveCount);

            StubSequentialCoordination(aggregatingSigner, ".custom");

            int exitCode = await SignInPlaceAsync(
                aggregatingSigner,
                maxConcurrency: 2,
                first,
                second);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(expected: 1, actual: getMaximumActiveCount());
        }

        [Fact]
        public async Task SignAsync_ClickOnceExtensionWithoutSequentialCoordination_RunsConcurrently()
        {
            FileInfo first = CreateInput("First.application");
            FileInfo second = CreateInput("Second.application");
            IAggregatingDataFormatSigner aggregatingSigner =
                CreateActivityTrackingSigner(
                    overlapWindow: TimeSpan.FromSeconds(10),
                    out Func<int> getMaximumActiveCount);

            int exitCode = await SignInPlaceAsync(
                aggregatingSigner,
                maxConcurrency: 2,
                first,
                second);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(expected: 2, actual: getMaximumActiveCount());
        }

        [Theory]
        [InlineData(".custom", true)]
        [InlineData(".application", false)]
        public async Task SignAsync_InPlace_SignsOnlyInputsRequiringSequentialCoordinationLast(
            string extension,
            bool requiresSequentialCoordination)
        {
            FileInfo first = CreateInput($"App{extension}");
            FileInfo payload = CreateInput("Payload.dll");
            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            List<string> signedFileExtensions = new();

            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(true);
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);

            if (requiresSequentialCoordination)
            {
                StubSequentialCoordination(aggregatingSigner, extension);
            }

            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    call =>
                    {
                        signedFileExtensions.Add(
                            call.Arg<SigningFile>().File.Extension);

                        return Task.CompletedTask;
                    });

            int exitCode = await SignInPlaceAsync(
                aggregatingSigner,
                maxConcurrency: 1,
                first,
                payload);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(
                requiresSequentialCoordination
                    ? new[] { ".dll", extension }
                    : new[] { extension, ".dll" },
                signedFileExtensions);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SignAsync_WithOutput_CopyWithoutSigningDoesNotOverwriteSignedLayoutFile(
            bool isCopiedInputFirst)
        {
            FileInfo application = CreateInput("App.application");
            FileInfo manifest = CreateInput("App.exe.manifest");
            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();

            aggregatingSigner
                .HasSigningWork(
                    Arg.Is<FileInfo>(
                        file => file.Extension == application.Extension),
                    Arg.Any<SignOptions>())
                .Returns(true);
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            StubSequentialCoordination(aggregatingSigner, ".application");
            aggregatingSigner
                .When(
                    value => value.StageSigningDependencies(
                        Arg.Any<FileInfo>(),
                        Arg.Any<DirectoryInfo>(),
                        Arg.Any<SignOptions>()))
                .Do(
                    call => File.Copy(
                        manifest.FullName,
                        Path.Combine(
                            call.Arg<DirectoryInfo>().FullName,
                            manifest.Name)));
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    call =>
                    {
                        // Mirrors ClickOnce signing the application
                        // manifest in its staged layout.
                        File.AppendAllText(
                            Path.Combine(
                                call.Arg<SigningFile>().File.DirectoryName!,
                                manifest.Name),
                            "-signed");

                        return Task.CompletedTask;
                    });

            int exitCode = await SignAsync(
                aggregatingSigner,
                outputFile: "signed",
                maxConcurrency: 1,
                isCopiedInputFirst
                    ? new[] { manifest, application }
                    : new[] { application, manifest });

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(
                $"{manifest.Name}-signed",
                File.ReadAllText(
                    Path.Combine(
                        _temporaryDirectory.Directory.FullName,
                        "signed",
                        manifest.Name)));
        }

        [Fact]
        public async Task SignAsync_IsolatedInputs_ReleaseSnapshotsWhenEachInputCompletes()
        {
            FileInfo first = CreateInput("First.dll");
            FileInfo second = CreateInput("Second.dll");
            using DirectoryServiceStub directoryService = new();
            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            List<string> retainedResults = new();

            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(true);
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    call =>
                    {
                        FileInfo file = call.Arg<SigningFile>().File;

                        // Temporary files that still hold a signed result
                        // of an input that has already completed.
                        retainedResults.AddRange(
                            directoryService.Directories
                                .Where(directory => directory.Exists)
                                .SelectMany(
                                    directory => directory.EnumerateFiles(
                                        "*",
                                        SearchOption.AllDirectories))
                                .Where(
                                    temporaryFile => File.ReadAllText(
                                        temporaryFile.FullName)
                                        .EndsWith(
                                            "-signed",
                                            StringComparison.Ordinal))
                                .Select(
                                    temporaryFile => temporaryFile.FullName));
                        File.AppendAllText(file.FullName, "-signed");

                        return Task.CompletedTask;
                    });
            Signer signer = new(
                CreateServiceProvider(aggregatingSigner, directoryService),
                Substitute.For<ILogger<ISigner>>());

            int exitCode = await signer.SignAsync(
                new[] { first, second },
                outputFile: null,
                fileList: null,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 1,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Empty(retainedResults);
            Assert.Equal("First.dll-signed", File.ReadAllText(first.FullName));
            Assert.Equal(
                "Second.dll-signed",
                File.ReadAllText(second.FullName));
            Assert.NotEmpty(directoryService.Directories);
            Assert.All(
                directoryService.Directories,
                directory =>
                {
                    directory.Refresh();
                    Assert.False(directory.Exists);
                });
        }

        [Fact]
        public async Task SignAsync_IndependentDuplicateGroups_ReleaseSnapshotsPerGroup()
        {
            FileInfo first = CreateInput("First.dll");
            FileInfo firstDuplicate =
                new(first.FullName.ToUpperInvariant());
            FileInfo second = CreateInput("Second.dll");
            FileInfo secondDuplicate =
                new(second.FullName.ToUpperInvariant());
            using DirectoryServiceStub directoryService = new();
            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            List<string> retainedFirstResults = new();
            SigningSourceIdentity secondIdentity =
                SigningSourceIdentity.Capture(second);
            int signCount = 0;

            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(true);
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    call =>
                    {
                        SigningFile signingFile =
                            call.Arg<SigningFile>();
                        FileInfo file = signingFile.File;

                        if (signingFile.SourceIdentity.Equals(
                            secondIdentity))
                        {
                            retainedFirstResults.AddRange(
                                directoryService.Directories
                                    .Where(
                                        directory =>
                                            directory.Exists)
                                    .SelectMany(
                                        directory =>
                                            directory.EnumerateFiles(
                                                "*",
                                                SearchOption
                                                    .AllDirectories))
                                    .Where(
                                        temporaryFile =>
                                            File.ReadAllText(
                                                temporaryFile.FullName)
                                                .Contains(
                                                    "First.dll-signed",
                                                    StringComparison
                                                        .Ordinal))
                                    .Select(
                                        temporaryFile =>
                                            temporaryFile.FullName));
                        }

                        ++signCount;
                        File.AppendAllText(
                            file.FullName,
                            "-signed");

                        return Task.CompletedTask;
                    });
            Signer signer = new(
                CreateServiceProvider(
                    aggregatingSigner,
                    directoryService),
                Substitute.For<ILogger<ISigner>>());

            int exitCode = await signer.SignAsync(
                new[]
                {
                    first,
                    firstDuplicate,
                    second,
                    secondDuplicate
                },
                outputFile: null,
                fileList: null,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 1,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(expected: 2, actual: signCount);
            Assert.Empty(retainedFirstResults);
            Assert.Equal(
                "First.dll-signed",
                File.ReadAllText(first.FullName));
            Assert.Equal(
                "Second.dll-signed",
                File.ReadAllText(second.FullName));
            Assert.All(
                directoryService.Directories,
                directory =>
                {
                    directory.Refresh();
                    Assert.False(directory.Exists);
                });
        }

        [Fact]
        public async Task SignAsync_ClickOnceGroup_ReleasesSnapshotsAfterLastPotentialConsumerCompletes()
        {
            DirectoryInfo publish = _temporaryDirectory.Directory
                .CreateSubdirectory("publish");
            FileInfo first = new(
                Path.Combine(publish.FullName, "First.dll"));
            FileInfo application = new(
                Path.Combine(publish.FullName, "App.application"));
            FileInfo second = new(
                Path.Combine(publish.FullName, "Second.dll"));
            File.WriteAllText(first.FullName, first.Name);
            File.WriteAllText(application.FullName, application.Name);
            File.WriteAllText(second.FullName, second.Name);
            using DirectoryServiceStub directoryService = new();
            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            SigningSourceIdentity firstIdentity =
                SigningSourceIdentity.Capture(first);
            SigningSourceIdentity secondIdentity =
                SigningSourceIdentity.Capture(second);
            List<string> retainedFirstResults = new();
            bool firstResultWasReused = false;

            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(true);
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            StubSequentialCoordination(
                aggregatingSigner,
                ".application");
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    async call =>
                    {
                        SigningFile signingFile =
                            call.Arg<SigningFile>();
                        FileInfo file = signingFile.File;

                        if (string.Equals(
                            file.Extension,
                            application.Extension,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            SigningOperationResult firstResult =
                                await call
                                    .Arg<SigningOperationCoordinator>()
                                    .ExecuteAsync(
                                        firstIdentity,
                                        () => throw new InvalidOperationException());
                            FileInfo reused = firstResult.Materialize(
                                new FileInfo(
                                    Path.Combine(
                                        file.DirectoryName!,
                                        "First.reused")));
                            firstResultWasReused =
                                File.ReadAllText(reused.FullName) ==
                                $"{first.Name}-signed";
                        }
                        else if (signingFile.SourceIdentity.Equals(
                            secondIdentity))
                        {
                            retainedFirstResults.AddRange(
                                directoryService.Directories
                                    .Where(
                                        directory =>
                                            directory.Exists)
                                    .SelectMany(
                                        directory =>
                                            directory.EnumerateFiles(
                                                "*",
                                                SearchOption
                                                    .AllDirectories))
                                    .Where(
                                        temporaryFile =>
                                            File.ReadAllText(
                                                temporaryFile.FullName)
                                                .Contains(
                                                    $"{first.Name}-signed",
                                                    StringComparison
                                                        .Ordinal))
                                    .Select(
                                        temporaryFile =>
                                            temporaryFile.FullName));
                        }

                        File.AppendAllText(
                            file.FullName,
                            "-signed");
                    });
            Signer signer = new(
                CreateServiceProvider(
                    aggregatingSigner,
                    directoryService),
                Substitute.For<ILogger<ISigner>>());

            int exitCode = await signer.SignAsync(
                new[] { first, application, second },
                outputFile: "signed",
                fileList: null,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 1,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.True(firstResultWasReused);
            Assert.Empty(retainedFirstResults);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("signed")]
        public async Task SignAsync_DuplicateInputsConcurrently_SignsOnce(
            string? outputFile)
        {
            FileInfo input = CreateInput("App.dll");
            string unsignedContent =
                new('x', 4 * 1024 * 1024);
            long expectedLength =
                unsignedContent.Length + "-signed".Length;
            File.WriteAllText(
                input.FullName,
                unsignedContent);
            FileInfo[] inputs = Enumerable
                .Range(0, 32)
                .Select(
                    _ => new FileInfo(
                        input.FullName.ToUpperInvariant()))
                .Prepend(input)
                .ToArray();
            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            int signCount = 0;

            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(true);
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    call =>
                    {
                        Interlocked.Increment(ref signCount);
                        File.AppendAllText(
                            call.Arg<SigningFile>().File.FullName,
                            "-signed");

                        return Task.CompletedTask;
                    });

            int exitCode = await SignAsync(
                aggregatingSigner,
                outputFile,
                maxConcurrency: inputs.Length,
                inputs);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(expected: 1, actual: signCount);
            FileInfo signedFile = new(
                outputFile is null
                    ? input.FullName
                    : Path.Combine(
                        _temporaryDirectory.Directory.FullName,
                        outputFile,
                        input.Name));
            Assert.Equal(
                expectedLength,
                signedFile.Length);
            Assert.EndsWith(
                "-signed",
                File.ReadAllText(signedFile.FullName));
        }

        [Theory]
        [InlineData(null, 1)]
        [InlineData(null, 2)]
        [InlineData("signed", 1)]
        [InlineData("signed", 2)]
        public async Task SignAsync_HardLinkAliases_SignsEachLogicalPath(
            string? outputFile,
            int maxConcurrency)
        {
            FileInfo source = CreateInput("Source.dll");
            FileInfo alias = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    "Alias.dll"));
            HardLink.Create(alias.FullName, source.FullName);
            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            int signCount = 0;

            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(true);
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    call =>
                    {
                        Interlocked.Increment(ref signCount);
                        File.AppendAllText(
                            call.Arg<SigningFile>().File.FullName,
                            "-signed");

                        return Task.CompletedTask;
                    });

            int exitCode = await SignAsync(
                aggregatingSigner,
                outputFile,
                maxConcurrency,
                source,
                alias);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Equal(expected: 2, actual: signCount);

            DirectoryInfo outputDirectory = outputFile is null
                ? _temporaryDirectory.Directory
                : new DirectoryInfo(
                    Path.Combine(
                        _temporaryDirectory.Directory.FullName,
                        outputFile));
            Assert.Equal(
                "Source.dll-signed",
                File.ReadAllText(
                    Path.Combine(
                        outputDirectory.FullName,
                        source.Name)));
            Assert.Equal(
                "Source.dll-signed",
                File.ReadAllText(
                    Path.Combine(
                        outputDirectory.FullName,
                        alias.Name)));
        }

        [Fact]
        public async Task SignAsync_IsolatedInputRequestsFileOutsideScope_ReturnsFailed()
        {
            FileInfo input = CreateInput("App.dll");
            FileInfo other = CreateInput("Other.dll");
            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            TestLogger<ISigner> logger = new();
            bool otherOperationInvoked = false;

            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(true);
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    async call =>
                    {
                        await call.Arg<SigningOperationCoordinator>()
                            .ExecuteAsync(
                                SigningSourceIdentity.Capture(other),
                                () =>
                                {
                                    otherOperationInvoked = true;

                                    return Task.FromResult(other);
                                });
                    });
            Signer signer = new(
                CreateServiceProvider(aggregatingSigner),
                logger);

            int exitCode = await signer.SignAsync(
                new[] { input },
                outputFile: null,
                fileList: null,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 1,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Failed, exitCode);
            Assert.False(otherOperationInvoked);
            Assert.Contains(
                logger.Entries,
                entry =>
                    entry.LogLevel == LogLevel.Error &&
                    entry.Message ==
                        "A signing operation requested a file outside " +
                        "its scope.");
            Assert.Equal("App.dll", File.ReadAllText(input.FullName));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("signed")]
        public async Task SignAsync_IsolatedAndGroupedClickOnceInputs_SignEachFileOnce(
            string? outputFile)
        {
            // publish\App.application and publish\Payload.exe (grouped; the
            // ClickOnce operation claims Payload.exe too, reusing its result)
            // other\Other.application (isolated)
            // other\sub\Child.application (discovered by Other.application)
            DirectoryInfo publish = _temporaryDirectory.Directory
                .CreateSubdirectory("publish");
            DirectoryInfo otherSub = _temporaryDirectory.Directory
                .CreateSubdirectory(Path.Combine("other", "sub"));
            FileInfo application = new(
                Path.Combine(publish.FullName, "App.application"));
            FileInfo payload = new(
                Path.Combine(publish.FullName, "Payload.exe"));
            FileInfo otherApplication = new(
                Path.Combine(
                    otherSub.Parent!.FullName,
                    "Other.application"));
            FileInfo child = new(
                Path.Combine(otherSub.FullName, "Child.application"));

            File.WriteAllText(application.FullName, "application");
            File.WriteAllText(payload.FullName, "payload");
            File.WriteAllText(otherApplication.FullName, "other");
            File.WriteAllText(child.FullName, "child");

            using X509Certificate2 certificate =
                SelfIssuedCertificateCreator.CreateCertificate();
            IMageCli mageCli = Substitute.For<IMageCli>();
            IDataFormatSigner payloadSigner =
                Substitute.For<IDataFormatSigner>();
            ConcurrentQueue<string> signedPayloads = new();

            Task SignPayloads(IEnumerable<FileInfo> files)
            {
                foreach (FileInfo file in files)
                {
                    signedPayloads.Enqueue(file.Name);
                    File.AppendAllText(file.FullName, "-signed");
                }

                return Task.CompletedTask;
            }

            mageCli.RunAsync(Arg.Any<string>()).Returns(0);
            payloadSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(
                    call => string.Equals(
                        call.Arg<FileInfo>().Extension,
                        ".exe",
                        StringComparison.OrdinalIgnoreCase));
            payloadSigner
                .SignAsync(
                    Arg.Any<IEnumerable<FileInfo>>(),
                    Arg.Any<SignOptions>())
                .Returns(
                    call => SignPayloads(
                        call.Arg<IEnumerable<FileInfo>>()));
            payloadSigner
                .SignAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    call => SignPayloads(
                        new[] { call.Arg<SigningFile>().File }));

            AggregatingSigner aggregatingSigner = CreateClickOnceSigner(
                certificate,
                mageCli,
                payloadSigner);

            int exitCode = await SignAsync(
                aggregatingSigner,
                outputFile,
                maxConcurrency: 2,
                application,
                payload,
                otherApplication);

            Assert.Equal(ExitCode.Success, exitCode);
            Assert.Single(signedPayloads);
            Assert.Equal(
                "payload-signed",
                File.ReadAllText(
                    outputFile is null
                        ? payload.FullName
                        : Path.Combine(
                            _temporaryDirectory.Directory.FullName,
                            outputFile,
                            "publish",
                            payload.Name)));

            string[] mageArguments = mageCli.ReceivedCalls()
                .Select(call => (string)call.GetArguments()[0]!)
                .ToArray();

            Assert.Equal(expected: 3, actual: mageArguments.Length);
            Assert.Single(
                mageArguments,
                arguments => arguments.Contains(
                    $"{Path.DirectorySeparatorChar}{application.Name}",
                    StringComparison.OrdinalIgnoreCase));
            Assert.Single(
                mageArguments,
                arguments => arguments.Contains(
                    $"{Path.DirectorySeparatorChar}{otherApplication.Name}",
                    StringComparison.OrdinalIgnoreCase));
            Assert.Single(
                mageArguments,
                arguments => arguments.Contains(
                    $@"sub{Path.DirectorySeparatorChar}{child.Name}",
                    StringComparison.OrdinalIgnoreCase));
        }

        private FileInfo CreateInput(string fileName)
        {
            FileInfo file = new(
                Path.Combine(
                    _temporaryDirectory.Directory.FullName,
                    fileName));

            File.WriteAllText(file.FullName, fileName);

            return file;
        }

        private static IAggregatingDataFormatSigner CreateActivityTrackingSigner(
            TimeSpan overlapWindow,
            out Func<int> getMaximumActiveCount)
        {
            IAggregatingDataFormatSigner aggregatingSigner =
                Substitute.For<IAggregatingDataFormatSigner>();
            TaskCompletionSource bothStarted = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int activeCount = 0;
            int maximumActiveCount = 0;

            aggregatingSigner
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    Arg.Any<SignOptions>())
                .Returns(true);
            aggregatingSigner
                .CanSign(Arg.Any<FileInfo>())
                .Returns(true);
            aggregatingSigner
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    Arg.Any<SignOptions>(),
                    Arg.Any<SigningOperationCoordinator>())
                .Returns(
                    async _ =>
                    {
                        int active =
                            Interlocked.Increment(ref activeCount);
                        int observed;

                        do
                        {
                            observed = Volatile.Read(
                                ref maximumActiveCount);
                        }
                        while (active > observed &&
                            Interlocked.CompareExchange(
                                ref maximumActiveCount,
                                active,
                                observed) != observed);

                        if (active == 2)
                        {
                            bothStarted.TrySetResult();
                        }

                        // Overlapping operations meet here; sequential
                        // ones wait out the window one at a time.
                        await Task.WhenAny(
                            bothStarted.Task,
                            Task.Delay(overlapWindow));
                        Interlocked.Decrement(ref activeCount);
                    });

            getMaximumActiveCount =
                () => Volatile.Read(ref maximumActiveCount);

            return aggregatingSigner;
        }

        // ClickOnceSigner disposes the certificate and key it receives, so
        // every call returns new instances.
        private static AggregatingSigner CreateClickOnceSigner(
            X509Certificate2 certificate,
            IMageCli mageCli,
            params IDataFormatSigner[] otherSigners)
        {
            ISignatureAlgorithmProvider signatureAlgorithmProvider =
                Substitute.For<ISignatureAlgorithmProvider>();
            ICertificateProvider certificateProvider =
                Substitute.For<ICertificateProvider>();
            IServiceProvider clickOnceServiceProvider =
                Substitute.For<IServiceProvider>();

            certificateProvider
                .GetCertificateAsync(Arg.Any<CancellationToken>())
                .Returns(_ => new X509Certificate2(certificate));
            signatureAlgorithmProvider
                .GetRsaAsync(Arg.Any<CancellationToken>())
                .Returns(_ => certificate.GetRSAPrivateKey()!);

            ClickOnceSigner clickOnceSigner = new(
                signatureAlgorithmProvider,
                certificateProvider,
                clickOnceServiceProvider,
                mageCli,
                Substitute.For<IManifestSigner>(),
                Substitute.For<ILogger<IDataFormatSigner>>(),
                Substitute.For<IFileMatcher>());
            AggregatingSigner aggregatingSigner = new(
                otherSigners.Prepend(clickOnceSigner).ToArray(),
                Substitute.For<IDefaultDataFormatSigner>(),
                Substitute.For<IContainerProvider>(),
                Substitute.For<IFileMetadataService>(),
                Substitute.For<IMatcherFactory>());

            clickOnceServiceProvider
                .GetService(typeof(IAggregatingDataFormatSigner))
                .Returns(aggregatingSigner);

            return aggregatingSigner;
        }

        private static void StubSequentialCoordination(
            IAggregatingDataFormatSigner aggregatingSigner,
            string extension)
        {
            aggregatingSigner
                .IsSequentialCoordinationRequired(
                    Arg.Is<FileInfo>(
                        file => string.Equals(
                            file.Extension,
                            extension,
                            StringComparison.OrdinalIgnoreCase)))
                .Returns(true);
        }

        private Task<int> SignInPlaceAsync(
            IAggregatingDataFormatSigner aggregatingSigner,
            int maxConcurrency,
            params FileInfo[] inputs)
        {
            return SignAsync(
                aggregatingSigner,
                outputFile: null,
                maxConcurrency,
                inputs);
        }

        private Task<int> SignAsync(
            IAggregatingDataFormatSigner aggregatingSigner,
            string? outputFile,
            int maxConcurrency,
            params FileInfo[] inputs)
        {
            Signer signer = new(
                CreateServiceProvider(aggregatingSigner),
                Substitute.For<ILogger<ISigner>>());

            return signer.SignAsync(
                inputs,
                outputFile,
                fileList: null,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: null,
                publisherName: null,
                description: null,
                descriptionUrl: null,
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);
        }

        private IServiceProvider CreateServiceProvider(
            IAggregatingDataFormatSigner aggregatingSigner,
            IDirectoryService? directoryService = null)
        {
            ICertificateProvider certificateProvider =
                Substitute.For<ICertificateProvider>();
            ICertificateVerifier certificateVerifier =
                Substitute.For<ICertificateVerifier>();
            IServiceProvider serviceProvider =
                Substitute.For<IServiceProvider>();

            certificateProvider
                .GetCertificateAsync(
                    Arg.Any<CancellationToken>())
                .Returns(
                    _ => SelfIssuedCertificateCreator.CreateCertificate());
            serviceProvider
                .GetService(Arg.Any<Type>())
                .Returns(
                    call =>
                    {
                        Type serviceType = call.Arg<Type>();

                        if (serviceType ==
                            typeof(IAggregatingDataFormatSigner))
                        {
                            return aggregatingSigner;
                        }

                        if (serviceType == typeof(IDirectoryService))
                        {
                            return directoryService ?? _directoryService;
                        }

                        if (serviceType ==
                            typeof(ICertificateProvider))
                        {
                            return certificateProvider;
                        }

                        if (serviceType ==
                            typeof(ICertificateVerifier))
                        {
                            return certificateVerifier;
                        }

                        return null;
                    });

            return serviceProvider;
        }

        [Fact]
        public async Task SignAsync_WhenFileIsPortableExecutable_Signs()
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);
            FileInfo file = new(Path.Combine(_temporaryDirectory.Directory.FullName, thisAssemblyFile.Name));

            File.Copy(thisAssemblyFile.FullName, file.FullName);

            FileInfo outputFile = new(Path.Combine(_temporaryDirectory.Directory.FullName, "signed.dll"));

            await SignAsync(_temporaryDirectory, file, outputFile);

            await VerifyAuthenticodeSignedFileAsync(outputFile);
        }

        [Fact]
        public async Task SignAsync_WhenFileIsPowerShellScript_Signs()
        {
            FileInfo file = new(Path.Combine(_temporaryDirectory.Directory.FullName, "script.ps1"));

            File.WriteAllText(file.FullName, "Write-Host 'Hello, World!'");

            FileInfo outputFile = new(Path.Combine(_temporaryDirectory.Directory.FullName, "signed.ps1"));

            await SignAsync(_temporaryDirectory, file, outputFile);

            SignedCms signedCms = GetSignedCmsFromPowerShellScript(outputFile);

            await VerifySignedCmsAsync(signedCms);
        }

        [Fact]
        public async Task SignAsync_WhenFileIsVsix_Signs()
        {
            FileInfo file = TestAssets.GetTestAsset(_temporaryDirectory.Directory, "VsixPackage.vsix");
            FileInfo outputFile = new(Path.Combine(_temporaryDirectory.Directory.FullName, "signed.vsix"));

            await SignAsync(_temporaryDirectory, file, outputFile);

            await VerifyVsixAsync(outputFile, _temporaryDirectory);
        }

        [Fact]
        public async Task SignAsync_WhenFileIsMsixBundle_Signs()
        {
            FileInfo file = TestAssets.GetTestAsset(_temporaryDirectory.Directory, "App1_1.0.0.0_x64.msixbundle");
            FileInfo outputFile = new(Path.Combine(_temporaryDirectory.Directory.FullName, "signed.msixbundle"));

            await SignAsync(_temporaryDirectory, file, outputFile);

            await VerifyMsixBundleFileAsync(outputFile, _temporaryDirectory);
        }

        [RequiresX64Fact]
        public async Task SignAsync_WhenFileIsApp_Signs()
        {
            FileInfo file = TestAssets.GetTestAsset(_temporaryDirectory.Directory, "EmptyExtension.app");
            FileInfo outputFile = new(Path.Combine(_temporaryDirectory.Directory.FullName, "signed.app"));

            await SignAsync(_temporaryDirectory, file, outputFile);

            await VerifyAppSignatureAsync(file, outputFile, _temporaryDirectory);
        }

        [Fact]
        public async Task SignAsync_WhenSigningSingleFile_WithOutputDirectoryName_Signs_ToOutputDirectory()
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);

            FileInfo file1 = new(Path.Combine(_temporaryDirectory.Directory.FullName, thisAssemblyFile.Name));
            var files = new[] { file1 };

            foreach (var file in files)
            {
                File.Copy(thisAssemblyFile.FullName, file.FullName);
            }

            var outputDirectory = Path.Combine(_temporaryDirectory.Directory.FullName, "signedFileNameWithoutExtensionIsTreatedAsDirectory");

            await SignAsync(_temporaryDirectory, files, outputDirectory);

            var outputFiles = new FileInfo[]
            {
                  new(Path.Combine(outputDirectory, file1.Name))
            };

            foreach (var outputFile in outputFiles)
            {
                Assert.True(File.Exists(outputFile.FullName));
                await VerifyAuthenticodeSignedFileAsync(outputFile);
            }
        }

        [Fact]
        public async Task SignAsync_WhenSigningMultipleFiles_WithOutputDirectoryName_Signs_ToOutputDirectory()
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);

            FileInfo file1 = new(Path.Combine(_temporaryDirectory.Directory.FullName, thisAssemblyFile.Name));
            FileInfo file2 = new(Path.Combine(_temporaryDirectory.Directory.FullName, Path.ChangeExtension(thisAssemblyFile.Name, ".Copy.dll")));
            var files = new[] { file1, file2 };

            foreach (var file in files)
            {
                File.Copy(thisAssemblyFile.FullName, file.FullName);
            }

            var outputDirectory = Path.Combine(_temporaryDirectory.Directory.FullName, "signedFiles.Directory.WithExtension.dll");

            await SignAsync(_temporaryDirectory, files, outputDirectory);

            var outputFiles = new FileInfo[]
            {
                  new(Path.Combine(outputDirectory, file1.Name)),
                  new(Path.Combine(outputDirectory, file2.Name))
            };

            foreach (var outputFile in outputFiles)
            {
                Assert.True(File.Exists(outputFile.FullName));
                await VerifyAuthenticodeSignedFileAsync(outputFile);
            }
        }

        [Fact]
        public async Task SignAsync_WhenSigningMultipleFiles_WithoutOutputDirectoryName_Signs_Inplace()
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);

            FileInfo file1 = new(Path.Combine(_temporaryDirectory.Directory.FullName, thisAssemblyFile.Name));
            FileInfo file2 = new(Path.Combine(_temporaryDirectory.Directory.FullName, Path.ChangeExtension(thisAssemblyFile.Name, ".Copy.dll")));
            var files = new[] { file1, file2 };

            foreach (var file in files)
            {
                File.Copy(thisAssemblyFile.FullName, file.FullName);
            }

            var emptyOutputDirectoryParameter = string.Empty;

            await SignAsync(_temporaryDirectory, files, emptyOutputDirectoryParameter);

            var outputFiles = new FileInfo[]
            {
                  file1,
                  file2
            };

            foreach (var outputFile in outputFiles)
            {
                await VerifyAuthenticodeSignedFileAsync(outputFile);
            }
        }

        [Fact]
        public async Task SignAsync_WhenFileListProvided_OnlyFiltersStandaloneFiles()
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);

            FileInfo file1 = new(Path.Combine(_temporaryDirectory.Directory.FullName, thisAssemblyFile.Name));
            FileInfo file2 = new(Path.Combine(_temporaryDirectory.Directory.FullName, Path.ChangeExtension(thisAssemblyFile.Name, ".Copy.dll")));
            var files = new[] { file1, file2 };

            foreach (var file in files)
            {
                File.Copy(thisAssemblyFile.FullName, file.FullName);
            }

            // Only file1 is listed in the file list.
            FileInfo fileList = new(Path.Combine(_temporaryDirectory.Directory.FullName, "filelist.txt"));
            File.WriteAllText(fileList.FullName, file1.Name);

            byte[] file2HashBefore = SHA256.HashData(File.ReadAllBytes(file2.FullName));

            ServiceProvider serviceProvider = Create();
            TestLogger<ISigner> logger = new();
            Signer signer = new(serviceProvider, logger);

            int exitCode = await signer.SignAsync(
                files,
                outputFile: string.Empty,
                fileList: fileList,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: "a",
                publisherName: null,
                description: "b",
                new Uri("https://description.test"),
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 4,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);

            // file1 is in the file list, so it must be signed.
            await VerifyAuthenticodeSignedFileAsync(file1);

            // file2 is not in the file list, so it must be left untouched.
            byte[] file2HashAfter = SHA256.HashData(File.ReadAllBytes(file2.FullName));
            Assert.Equal(file2HashBefore, file2HashAfter);
        }

        [Fact]
        public async Task SignAsync_WhenFileListHasOnlyExclusions_DoesNotSignAnyStandaloneFiles()
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);

            FileInfo file1 = new(Path.Combine(_temporaryDirectory.Directory.FullName, thisAssemblyFile.Name));
            FileInfo file2 = new(Path.Combine(_temporaryDirectory.Directory.FullName, Path.ChangeExtension(thisAssemblyFile.Name, ".Copy.dll")));
            var files = new[] { file1, file2 };

            foreach (var file in files)
            {
                File.Copy(thisAssemblyFile.FullName, file.FullName);
            }

            // An exclusion-only file list does not implicitly include everything.
            FileInfo fileList = new(Path.Combine(_temporaryDirectory.Directory.FullName, "filelist.txt"));
            File.WriteAllText(fileList.FullName, $"!{file2.Name}");

            byte[] file1HashBefore = SHA256.HashData(File.ReadAllBytes(file1.FullName));
            byte[] file2HashBefore = SHA256.HashData(File.ReadAllBytes(file2.FullName));

            ServiceProvider serviceProvider = Create();
            TestLogger<ISigner> logger = new();
            Signer signer = new(serviceProvider, logger);

            int exitCode = await signer.SignAsync(
                files,
                outputFile: string.Empty,
                fileList: fileList,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: "a",
                publisherName: null,
                description: "b",
                new Uri("https://description.test"),
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 4,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);

            Assert.Equal(file1HashBefore, SHA256.HashData(File.ReadAllBytes(file1.FullName)));
            Assert.Equal(file2HashBefore, SHA256.HashData(File.ReadAllBytes(file2.FullName)));
        }

        [Fact]
        public async Task SignAsync_WhenFileListIncludesAllAndExcludesOne_SignsOnlyTheNonExcludedFile()
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);

            FileInfo file1 = new(Path.Combine(_temporaryDirectory.Directory.FullName, thisAssemblyFile.Name));
            FileInfo file2 = new(Path.Combine(_temporaryDirectory.Directory.FullName, Path.ChangeExtension(thisAssemblyFile.Name, ".Copy.dll")));
            var files = new[] { file1, file2 };

            foreach (var file in files)
            {
                File.Copy(thisAssemblyFile.FullName, file.FullName);
            }

            // Include everything, then exclude file2.
            FileInfo fileList = new(Path.Combine(_temporaryDirectory.Directory.FullName, "filelist.txt"));
            File.WriteAllText(fileList.FullName, $"**/*{Environment.NewLine}!{file2.Name}");

            byte[] file2HashBefore = SHA256.HashData(File.ReadAllBytes(file2.FullName));

            ServiceProvider serviceProvider = Create();
            TestLogger<ISigner> logger = new();
            Signer signer = new(serviceProvider, logger);

            int exitCode = await signer.SignAsync(
                files,
                outputFile: string.Empty,
                fileList: fileList,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: "a",
                publisherName: null,
                description: "b",
                new Uri("https://description.test"),
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 4,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);

            // file1 matches the include-all pattern and is not excluded, so it must be signed.
            await VerifyAuthenticodeSignedFileAsync(file1);

            // file2 is explicitly excluded, so it must be left untouched.
            Assert.Equal(file2HashBefore, SHA256.HashData(File.ReadAllBytes(file2.FullName)));
        }

        [Fact]
        public async Task SignAsync_WhenFileListProvided_AndInputIsContainer_SignsContainerContents()
        {
            FileInfo file = TestAssets.GetTestAsset(_temporaryDirectory.Directory, "VsixPackage.vsix");

            // The file list targets files inside the container, not the container itself.
            FileInfo fileList = new(Path.Combine(_temporaryDirectory.Directory.FullName, "filelist.txt"));
            File.WriteAllText(fileList.FullName, "**/*.dll");

            ServiceProvider serviceProvider = Create();
            TestLogger<ISigner> logger = new();
            Signer signer = new(serviceProvider, logger);

            int exitCode = await signer.SignAsync(
                new[] { file },
                outputFile: string.Empty,
                fileList: fileList,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: "a",
                publisherName: null,
                description: "b",
                new Uri("https://description.test"),
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 4,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);

            await VerifyVsixAsync(file, _temporaryDirectory);
        }

        [Fact]
        public async Task SignAsync_WhenFileListMatchesFilesInSubdirectory_SignsOnlyTheMatchingFile()
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);

            DirectoryInfo subdirectory = Directory.CreateDirectory(Path.Combine(_temporaryDirectory.Directory.FullName, "sub"));

            FileInfo file1 = new(Path.Combine(subdirectory.FullName, thisAssemblyFile.Name));
            FileInfo file2 = new(Path.Combine(_temporaryDirectory.Directory.FullName, Path.ChangeExtension(thisAssemblyFile.Name, ".Copy.dll")));
            var files = new[] { file1, file2 };

            foreach (var file in files)
            {
                File.Copy(thisAssemblyFile.FullName, file.FullName);
            }

            // Only the files under the sub directory are listed.
            FileInfo fileList = new(Path.Combine(_temporaryDirectory.Directory.FullName, "filelist.txt"));
            File.WriteAllText(fileList.FullName, "sub/*.dll");

            byte[] file2HashBefore = SHA256.HashData(File.ReadAllBytes(file2.FullName));

            ServiceProvider serviceProvider = Create();
            TestLogger<ISigner> logger = new();
            Signer signer = new(serviceProvider, logger);

            int exitCode = await signer.SignAsync(
                files,
                outputFile: string.Empty,
                fileList: fileList,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: "a",
                publisherName: null,
                description: "b",
                new Uri("https://description.test"),
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 4,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);

            // file1 is under sub/ and matches the pattern, so it must be signed.
            await VerifyAuthenticodeSignedFileAsync(file1);

            // file2 is outside sub/, so it must be left untouched.
            Assert.Equal(file2HashBefore, SHA256.HashData(File.ReadAllBytes(file2.FullName)));
        }

        [Fact]
        public async Task SignAsync_WhenFileListProvided_WithContainerAndStandaloneFile_FiltersOnlyTheStandaloneFile()
        {
            FileInfo container = TestAssets.GetTestAsset(_temporaryDirectory.Directory, "VsixPackage.vsix");

            FileInfo script = new(Path.Combine(_temporaryDirectory.Directory.FullName, "script.ps1"));
            File.WriteAllText(script.FullName, "Write-Host 'Hello, World!'");

            // The file list matches only dlls, so the script is not selected.
            FileInfo fileList = new(Path.Combine(_temporaryDirectory.Directory.FullName, "filelist.txt"));
            File.WriteAllText(fileList.FullName, "**/*.dll");

            byte[] scriptHashBefore = SHA256.HashData(File.ReadAllBytes(script.FullName));

            ServiceProvider serviceProvider = Create();
            TestLogger<ISigner> logger = new();
            Signer signer = new(serviceProvider, logger);

            int exitCode = await signer.SignAsync(
                new[] { container, script },
                outputFile: string.Empty,
                fileList: fileList,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: "a",
                publisherName: null,
                description: "b",
                new Uri("https://description.test"),
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 4,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);

            // The container is not filtered at the top level, so its contents are signed.
            await VerifyVsixAsync(container, _temporaryDirectory);

            // The script does not match the file list, so it must be left untouched.
            Assert.Equal(scriptHashBefore, SHA256.HashData(File.ReadAllBytes(script.FullName)));
        }

        [Fact]
        public async Task SignAsync_WhenFileListProvided_AndInputPathDiffersInCaseFromBaseDirectory_SignsTheFile()
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);
            FileInfo file = new(Path.Combine(_temporaryDirectory.Directory.FullName, thisAssemblyFile.Name));

            File.Copy(thisAssemblyFile.FullName, file.FullName);

            FileInfo fileList = new(Path.Combine(_temporaryDirectory.Directory.FullName, "filelist.txt"));
            File.WriteAllText(fileList.FullName, "**/*.dll");

            // Same file on disk, but the base directory part of the path is spelled in upper case.
            string upperCaseBase = _temporaryDirectory.Directory.FullName.ToUpperInvariant();
            FileInfo upperCaseInput = new(Path.Combine(upperCaseBase, file.Name));

            ServiceProvider serviceProvider = Create();
            TestLogger<ISigner> logger = new();
            Signer signer = new(serviceProvider, logger);

            int exitCode = await signer.SignAsync(
                new[] { upperCaseInput },
                outputFile: string.Empty,
                fileList: fileList,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: "a",
                publisherName: null,
                description: "b",
                new Uri("https://description.test"),
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 4,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);

            // The input is the same file, so it must be signed rather than silently skipped.
            await VerifyAuthenticodeSignedFileAsync(file);
        }

        [Fact]
        public async Task SignAsync_WhenFileListProvided_AndSubdirectoryCaseDiffersFromPattern_SignsTheFile()
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);

            Directory.CreateDirectory(Path.Combine(_temporaryDirectory.Directory.FullName, "sub"));

            FileInfo file = new(Path.Combine(_temporaryDirectory.Directory.FullName, "sub", thisAssemblyFile.Name));

            File.Copy(thisAssemblyFile.FullName, file.FullName);

            FileInfo fileList = new(Path.Combine(_temporaryDirectory.Directory.FullName, "filelist.txt"));
            File.WriteAllText(fileList.FullName, "sub/*.dll");

            // Same file on disk, but the subdirectory is spelled in upper case.
            FileInfo input = new(Path.Combine(_temporaryDirectory.Directory.FullName, "SUB", thisAssemblyFile.Name));

            int exitCode = await SignWithFileListAsync(new[] { input }, fileList, _temporaryDirectory.Directory);

            Assert.Equal(ExitCode.Success, exitCode);

            await VerifyAuthenticodeSignedFileAsync(file);
        }

        [Fact]
        public async Task SignAsync_WhenFileListProvided_AndInputIsOutsideBaseDirectory_DoesNotSignTheFile()
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);

            DirectoryInfo baseDirectory = Directory.CreateDirectory(Path.Combine(_temporaryDirectory.Directory.FullName, "base"));

            FileInfo outside = new(Path.Combine(_temporaryDirectory.Directory.FullName, thisAssemblyFile.Name));

            File.Copy(thisAssemblyFile.FullName, outside.FullName);

            FileInfo fileList = new(Path.Combine(baseDirectory.FullName, "filelist.txt"));
            File.WriteAllText(fileList.FullName, "**/*.dll");

            byte[] hashBefore = SHA256.HashData(File.ReadAllBytes(outside.FullName));

            int exitCode = await SignWithFileListAsync(new[] { outside }, fileList, baseDirectory);

            Assert.Equal(ExitCode.Success, exitCode);

            // The input is not under the base directory, so the file list does not select it.
            Assert.Equal(hashBefore, SHA256.HashData(File.ReadAllBytes(outside.FullName)));
        }

        [Fact]
        public async Task SignAsync_WhenFileListExclusionDiffersInCase_ExcludesTheFile()
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);

            FileInfo file1 = new(Path.Combine(_temporaryDirectory.Directory.FullName, thisAssemblyFile.Name));
            FileInfo file2 = new(Path.Combine(_temporaryDirectory.Directory.FullName, Path.ChangeExtension(thisAssemblyFile.Name, ".Copy.dll")));
            var files = new[] { file1, file2 };

            foreach (var file in files)
            {
                File.Copy(thisAssemblyFile.FullName, file.FullName);
            }

            // The exclusion is spelled in upper case, the file on disk is not.
            FileInfo fileList = new(Path.Combine(_temporaryDirectory.Directory.FullName, "filelist.txt"));
            File.WriteAllText(fileList.FullName, $"**/*{Environment.NewLine}!{file2.Name.ToUpperInvariant()}");

            byte[] file2HashBefore = SHA256.HashData(File.ReadAllBytes(file2.FullName));

            int exitCode = await SignWithFileListAsync(files, fileList, _temporaryDirectory.Directory);

            Assert.Equal(ExitCode.Success, exitCode);

            await VerifyAuthenticodeSignedFileAsync(file1);

            Assert.Equal(file2HashBefore, SHA256.HashData(File.ReadAllBytes(file2.FullName)));
        }

        [Fact]
        public async Task SignAsync_WhenFileListProvided_AndFileNameHasSpacesAndBrackets_SignsTheFile()
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);
            FileInfo file = new(Path.Combine(_temporaryDirectory.Directory.FullName, "my app [1] (copy).dll"));

            File.Copy(thisAssemblyFile.FullName, file.FullName);

            FileInfo fileList = new(Path.Combine(_temporaryDirectory.Directory.FullName, "filelist.txt"));
            File.WriteAllText(fileList.FullName, "**/*.dll");

            int exitCode = await SignWithFileListAsync(new[] { file }, fileList, _temporaryDirectory.Directory);

            Assert.Equal(ExitCode.Success, exitCode);

            await VerifyAuthenticodeSignedFileAsync(file);
        }

        [Theory]
        [InlineData("signed")]
        [InlineData("signed.v1")]
        public async Task SignAsync_WhenFileListFiltersMultipleInputsToOne_WithOutputDirectory_PreservesRelativePath(string outputDirectoryName)
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);

            DirectoryInfo subdirectory = Directory.CreateDirectory(Path.Combine(_temporaryDirectory.Directory.FullName, "sub"));

            FileInfo file1 = new(Path.Combine(subdirectory.FullName, thisAssemblyFile.Name));
            FileInfo file2 = new(Path.Combine(_temporaryDirectory.Directory.FullName, Path.ChangeExtension(thisAssemblyFile.Name, ".Copy.dll")));
            var files = new[] { file1, file2 };

            foreach (var file in files)
            {
                File.Copy(thisAssemblyFile.FullName, file.FullName);
            }

            // Only the file under sub/ is selected, so two inputs are filtered down to one.
            FileInfo fileList = new(Path.Combine(_temporaryDirectory.Directory.FullName, "filelist.txt"));
            File.WriteAllText(fileList.FullName, "sub/*.dll");

            ServiceProvider serviceProvider = Create();
            TestLogger<ISigner> logger = new();
            Signer signer = new(serviceProvider, logger);

            int exitCode = await signer.SignAsync(
                files,
                outputFile: outputDirectoryName,
                fileList: fileList,
                recurseContainers: true,
                _temporaryDirectory.Directory,
                applicationName: "a",
                publisherName: null,
                description: "b",
                new Uri("https://description.test"),
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 4,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);

            // Multi-input rules apply, so the output is a directory and sub/ is preserved.
            FileInfo expectedOutput = new(Path.Combine(_temporaryDirectory.Directory.FullName, outputDirectoryName, "sub", file1.Name));

            Assert.True(File.Exists(expectedOutput.FullName));
            await VerifyAuthenticodeSignedFileAsync(expectedOutput);

            // The relative path must not be flattened.
            Assert.False(File.Exists(Path.Combine(_temporaryDirectory.Directory.FullName, outputDirectoryName, file1.Name)));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public async Task SignAsync_WhenFileListProvided_DoesNotScanUnreadableFoldersInBaseDirectory()
        {
            FileInfo thisAssemblyFile = new(typeof(SignerTests).Assembly.Location);
            FileInfo file = new(Path.Combine(_temporaryDirectory.Directory.FullName, thisAssemblyFile.Name));

            File.Copy(thisAssemblyFile.FullName, file.FullName);

            // The ** pattern would make a directory scan enter every subdirectory of the base directory.
            FileInfo fileList = new(Path.Combine(_temporaryDirectory.Directory.FullName, "filelist.txt"));
            File.WriteAllText(fileList.FullName, "**/*.dll");

            // This folder is not an input and nobody can list it.
            DirectoryInfo restricted = Directory.CreateDirectory(Path.Combine(_temporaryDirectory.Directory.FullName, "restricted"));
            FileSystemAccessRule denyList = new(
                new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null),
                FileSystemRights.ListDirectory,
                AccessControlType.Deny);

            DirectorySecurity security = restricted.GetAccessControl();
            security.AddAccessRule(denyList);
            restricted.SetAccessControl(security);

            try
            {
                ServiceProvider serviceProvider = Create();
                TestLogger<ISigner> logger = new();
                Signer signer = new(serviceProvider, logger);

                int exitCode = await signer.SignAsync(
                    new[] { file },
                    outputFile: null,
                    fileList: fileList,
                    recurseContainers: true,
                    _temporaryDirectory.Directory,
                    applicationName: "a",
                    publisherName: null,
                    description: "b",
                    new Uri("https://description.test"),
                    _certificatesFixture.TimestampServiceUrl,
                    maxConcurrency: 4,
                    HashAlgorithmName.SHA256,
                    HashAlgorithmName.SHA256);

                Assert.Equal(ExitCode.Success, exitCode);

                await VerifyAuthenticodeSignedFileAsync(file);
            }
            finally
            {
                // Restore access so the temporary directory can be deleted.
                security = restricted.GetAccessControl();
                security.RemoveAccessRule(denyList);
                restricted.SetAccessControl(security);
            }
        }

        private async Task<int> SignWithFileListAsync(IReadOnlyList<FileInfo> files, FileInfo fileList, DirectoryInfo baseDirectory)
        {
            ServiceProvider serviceProvider = Create();
            TestLogger<ISigner> logger = new();
            Signer signer = new(serviceProvider, logger);

            return await signer.SignAsync(
                files,
                outputFile: string.Empty,
                fileList: fileList,
                recurseContainers: true,
                baseDirectory,
                applicationName: "a",
                publisherName: null,
                description: "b",
                new Uri("https://description.test"),
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 4,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);
        }

        private async Task SignAsync(TemporaryDirectory temporaryDirectory, FileInfo file, FileInfo outputFile)
        {
            await SignAsync(temporaryDirectory, new[] { file }, outputFile.FullName);
        }

        private async Task SignAsync(TemporaryDirectory temporaryDirectory, IReadOnlyList<FileInfo> files, string outputFile)
        {
            ServiceProvider serviceProvider = Create();
            TestLogger<ISigner> logger = new();
            Signer signer = new(serviceProvider, logger);

            int exitCode = await signer.SignAsync(
                files,
                outputFile: outputFile,
                fileList: null,
                recurseContainers: true,
                temporaryDirectory.Directory,
                applicationName: "a",
                publisherName: null,
                description: "b",
                new Uri("https://description.test"),
                _certificatesFixture.TimestampServiceUrl,
                maxConcurrency: 4,
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256);

            Assert.Equal(ExitCode.Success, exitCode);

            TestLogEntry lastLogEntry = logger.Entries.Last();

            Assert.Equal(LogLevel.Information, lastLogEntry.LogLevel);
            Assert.Matches(@"^Completed in \d+ ms.$", lastLogEntry.Message);
        }

        private async Task VerifyAuthenticodeSignedFileAsync(FileInfo outputFile)
        {
            Assert.True(AuthenticodeSignatureReader.TryGetSignedCms(outputFile, out SignedCms? signedCms));

            await VerifySignedCmsAsync(signedCms);
        }

        private async Task VerifyMsixBundleFileAsync(FileInfo outputFile, TemporaryDirectory temporaryDirectory)
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

        private async Task VerifyAppSignatureAsync(FileInfo unsignedAppFile, FileInfo signedAppFile, TemporaryDirectory temporaryDirectory)
        {
            FileInfo signatureFile = new(Path.Combine(temporaryDirectory.Directory.FullName, "signature.p7s"));

            if (await TryExtractSignatureBlockAsync(unsignedAppFile, signedAppFile, signatureFile))
            {
                SignedCms signedCms = GetSignedCms(signatureFile);

                await VerifySignedCmsAsync(signedCms);
            }
            else
            {
                Assert.Fail("The file is not signed.");
            }
        }

        private static async Task<bool> TryExtractSignatureBlockAsync(
            FileInfo unsignedAppFile,
            FileInfo signedAppFile,
            FileInfo signatureFile)
        {
            // NAVX signature block marker
            ReadOnlyMemory<byte> nxsb = Encoding.UTF8.GetBytes("NXSB");

            long endOfUnsignedApp = unsignedAppFile.Length;

            using (BinaryReader reader = new(signedAppFile.OpenRead()))
            {
                reader.BaseStream.Seek(endOfUnsignedApp, SeekOrigin.Begin);

                byte[] bytes = reader.ReadBytes(nxsb.Length);

                if (nxsb.Span.SequenceEqual(bytes))
                {
                    using (FileStream signatureStream = signatureFile.OpenWrite())
                    {
                        await reader.BaseStream.CopyToAsync(signatureStream);

                        return true;
                    }
                }
            }

            return false;
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
                stream.ReadExactly(buffer.Span);
            }

            SignedCms signedCms = new();

            // The first 4 bytes are 0x504B4358 ("PKCX").
            signedCms.Decode(buffer[4..].Span);

            return signedCms;
        }

        private static SignedCms GetSignedCms(FileInfo file)
        {
            byte[] bytes = File.ReadAllBytes(file.FullName);
            SignedCms signedCms = new();

            signedCms.Decode(bytes);

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
            services.AddSingleton<ISignatureAlgorithmProvider>(_keyVaultServiceStub);
            services.AddSingleton<ICertificateProvider>(_keyVaultServiceStub);
            services.AddSingleton<IDataFormatSigner, AzureSignToolSigner>();
            services.AddSingleton<IDataFormatSigner, ClickOnceSigner>();
            services.AddSingleton<IDataFormatSigner, VsixSigner>();
            services.AddSingleton<IDataFormatSigner, NuGetSigner>();
            services.AddSingleton<IDataFormatSigner, AppInstallerServiceSigner>();
            services.AddSingleton<IDefaultDataFormatSigner, DefaultSigner>();
            services.AddSingleton<IAggregatingDataFormatSigner, AggregatingSigner>();
            services.AddSingleton<IManifestSigner, ManifestSigner>();
            services.AddSingleton<IMageCli, MageCli>();
            services.AddSingleton<IMakeAppxCli, MakeAppxCli>();
            services.AddSingleton<INuGetSignTool, NuGetSignTool>();
            services.AddSingleton<IVsixSignTool, VsixSignTool>();
            services.AddSingleton<ICertificateVerifier, CertificateVerifier>();
            services.AddSingleton<ISigner, Signer>();

            return new ServiceProvider(services.BuildServiceProvider());
        }
    }
}