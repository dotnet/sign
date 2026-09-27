// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Sign.Core.Test
{
    public sealed class SigningOperationExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_StagesSignsAndPublishesInOrder()
        {
            using TestDirectory sourceDirectory = new();
            using TestDirectory outputRoot = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService);
            FileInfo source = new(
                Path.Combine(sourceDirectory.FullPath, "source.application"));
            File.WriteAllText(source.FullName, "unsigned");
            FileInfo output = new(
                Path.Combine(
                    outputRoot.FullPath,
                    "nested",
                    "signed.application"));
            SigningOperationPlan plan = new(
                SigningFile.Capture(source),
                output);
            SignOptions options = new(
                HashAlgorithmName.SHA256,
                new Uri("https://timestamp.test"));
            IAggregatingDataFormatSigner signer =
                Substitute.For<IAggregatingDataFormatSigner>();
            List<string> operations = new();

            signer
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    options)
                .Returns(true);
            signer.CanSign(source).Returns(true);
            signer
                .When(
                    value => value.StageSigningDependencies(
                        Arg.Any<FileInfo>(),
                        Arg.Any<DirectoryInfo>(),
                        options))
                .Do(
                    call =>
                    {
                        Assert.False(output.Directory!.Exists);
                        operations.Add("stage");
                    });
            signer
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    options,
                    coordinator)
                .Returns(
                    call =>
                    {
                        SigningFile stagedFile =
                            call.Arg<SigningFile>();

                        Assert.Equal(
                            plan.Source.SourceIdentity,
                            stagedFile.SourceIdentity);
                        File.AppendAllText(
                            stagedFile.File.FullName,
                            "-signed");
                        operations.Add("sign");

                        return Task.CompletedTask;
                    });
            SigningOperationExecutor executor = new(
                signer,
                directoryService,
                Substitute.For<ILogger<ISigner>>(),
                coordinator);

            await executor.ExecuteAsync(plan, options);

            Assert.Equal(
                new[] { "stage", "sign" },
                operations);
            Assert.Equal(
                "unsigned-signed",
                File.ReadAllText(output.FullName));
        }

        [Fact]
        public async Task Stage_ArtifactRemainsAvailableUntilDisposed()
        {
            using TestDirectory sourceDirectory = new();
            using TestDirectory outputRoot = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService);
            FileInfo source = new(
                Path.Combine(
                    sourceDirectory.FullPath,
                    "source.application"));
            File.WriteAllText(source.FullName, "content");
            SigningOperationPlan plan = new(
                SigningFile.Capture(source),
                new FileInfo(
                    Path.Combine(outputRoot.FullPath, "output.bin")));
            SignOptions options = new(
                HashAlgorithmName.SHA256,
                new Uri("https://timestamp.test"));
            IAggregatingDataFormatSigner signer =
                Substitute.For<IAggregatingDataFormatSigner>();

            signer.IsOriginalFileNameRequired(source).Returns(true);
            SigningOperationExecutor executor = new(
                signer,
                directoryService,
                Substitute.For<ILogger<ISigner>>(),
                coordinator);

            SigningOperationStage stage = executor.Stage(plan, options);
            FileInfo stagedFile = stage.Input.File;
            DirectoryInfo stagingDirectory = stagedFile.Directory!;

            Assert.True(stagedFile.Exists);
            Assert.Equal(source.Name, stagedFile.Name);
            Assert.Equal(
                plan.Source.SourceIdentity,
                stage.Input.SourceIdentity);

            stage.Dispose();
            stagingDirectory.Refresh();

            Assert.False(stagingDirectory.Exists);
        }

        [Theory]
        [InlineData("source.custom", true)]
        [InlineData("source.application", false)]
        public async Task Stage_KeepsOriginalFileNameOnlyWhenSignerRequiresIt(
            string fileName,
            bool isOriginalFileNameRequired)
        {
            using TestDirectory sourceDirectory = new();
            using TestDirectory outputRoot = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService);
            FileInfo source = new(
                Path.Combine(sourceDirectory.FullPath, fileName));
            File.WriteAllText(source.FullName, "content");
            SigningOperationPlan plan = new(
                SigningFile.Capture(source),
                new FileInfo(
                    Path.Combine(outputRoot.FullPath, "output.bin")));
            SignOptions options = new(
                HashAlgorithmName.SHA256,
                new Uri("https://timestamp.test"));
            IAggregatingDataFormatSigner signer =
                Substitute.For<IAggregatingDataFormatSigner>();

            signer.CanSign(source).Returns(true);
            signer
                .IsOriginalFileNameRequired(source)
                .Returns(isOriginalFileNameRequired);
            SigningOperationExecutor executor = new(
                signer,
                directoryService,
                Substitute.For<ILogger<ISigner>>(),
                coordinator);

            using SigningOperationStage stage = executor.Stage(plan, options);
            FileInfo stagedFile = stage.Input.File;

            Assert.Equal(source.Extension, stagedFile.Extension);
            Assert.Equal(
                isOriginalFileNameRequired,
                string.Equals(
                    source.Name,
                    stagedFile.Name,
                    StringComparison.Ordinal));
        }

        [Fact]
        public async Task ExecuteAsync_DuplicateSourceWithDifferentOutputs_SignsOnce()
        {
            using TestDirectory sourceDirectory = new();
            using TestDirectory outputDirectory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService);
            FileInfo source = sourceDirectory.CreateFile(
                relativePath: "source.bin",
                contents: "unsigned");
            SigningFile signingFile = SigningFile.Capture(source);
            SigningOperationPlan first = new(
                signingFile,
                new FileInfo(
                    Path.Combine(
                        outputDirectory.FullPath,
                        "first",
                        "signed.bin")));
            SigningOperationPlan second = new(
                signingFile,
                new FileInfo(
                    Path.Combine(
                        outputDirectory.FullPath,
                        "second",
                        "signed.bin")));
            SignOptions options = new(
                HashAlgorithmName.SHA256,
                new Uri("https://timestamp.test"));
            IAggregatingDataFormatSigner signer =
                Substitute.For<IAggregatingDataFormatSigner>();
            int signingCount = 0;

            signer
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    options)
                .Returns(true);
            signer
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    options,
                    coordinator)
                .Returns(
                    call =>
                    {
                        SigningFile staged =
                            call.Arg<SigningFile>();
                        Interlocked.Increment(ref signingCount);
                        File.AppendAllText(
                            staged.File.FullName,
                            "-signed");

                        return Task.CompletedTask;
                    });
            SigningOperationExecutor executor = new(
                signer,
                directoryService,
                Substitute.For<ILogger<ISigner>>(),
                coordinator);

            await Task.WhenAll(
                executor.ExecuteAsync(first, options),
                executor.ExecuteAsync(second, options));

            Assert.Equal(expected: 1, actual: signingCount);
            Assert.Equal(
                "unsigned-signed",
                File.ReadAllText(first.Output.FullName));
            Assert.Equal(
                "unsigned-signed",
                File.ReadAllText(second.Output.FullName));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_StagingDirectoryHasExtraFile_PublishesSiblingsOnlyWhenDependenciesStaged(
            bool isDependencyStaged)
        {
            using TestDirectory sourceDirectory = new();
            using TestDirectory outputDirectory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService);
            FileInfo source = sourceDirectory.CreateFile(
                relativePath: "source.bin",
                contents: "unsigned");
            SigningOperationPlan plan = new(
                SigningFile.Capture(source),
                new FileInfo(
                    Path.Combine(outputDirectory.FullPath, "signed.bin")));
            SignOptions options = new(
                HashAlgorithmName.SHA256,
                new Uri("https://timestamp.test"));
            IAggregatingDataFormatSigner signer =
                Substitute.For<IAggregatingDataFormatSigner>();

            signer
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    options)
                .Returns(true);
            signer
                .When(
                    value => value.StageSigningDependencies(
                        Arg.Any<FileInfo>(),
                        Arg.Any<DirectoryInfo>(),
                        options))
                .Do(
                    call =>
                    {
                        if (isDependencyStaged)
                        {
                            File.WriteAllText(
                                Path.Combine(
                                    call.Arg<DirectoryInfo>().FullName,
                                    "dependency.bin"),
                                "dependency");
                        }
                    });
            signer
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    options,
                    coordinator)
                .Returns(
                    call =>
                    {
                        FileInfo staged = call.Arg<SigningFile>().File;

                        File.AppendAllText(staged.FullName, "-signed");
                        File.WriteAllText(
                            Path.Combine(
                                staged.DirectoryName!,
                                "sidecar.tmp"),
                            "sidecar");

                        return Task.CompletedTask;
                    });
            SigningOperationExecutor executor = new(
                signer,
                directoryService,
                Substitute.For<ILogger<ISigner>>(),
                coordinator);

            await executor.ExecuteAsync(plan, options);

            Assert.Equal(
                "unsigned-signed",
                File.ReadAllText(plan.Output.FullName));
            Assert.Equal(
                isDependencyStaged,
                File.Exists(
                    Path.Combine(
                        outputDirectory.FullPath,
                        "dependency.bin")));
            Assert.Equal(
                isDependencyStaged,
                File.Exists(
                    Path.Combine(
                        outputDirectory.FullPath,
                        "sidecar.tmp")));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_WhenFileHasNoSigningWork_CopiesWithoutClaimingIdentity(
            bool inPlace)
        {
            using TestDirectory sourceDirectory = new();
            using TestDirectory outputDirectory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService);
            FileInfo source = sourceDirectory.CreateFile(
                relativePath: "App.exe.manifest",
                contents: "unsigned");
            SigningOperationPlan plan = new(
                SigningFile.Capture(source),
                inPlace
                    ? source
                    : new FileInfo(
                        Path.Combine(
                            outputDirectory.FullPath,
                            "nested",
                            source.Name)));
            SignOptions options = new(
                HashAlgorithmName.SHA256,
                new Uri("https://timestamp.test"));
            IAggregatingDataFormatSigner signer =
                Substitute.For<IAggregatingDataFormatSigner>();
            SigningOperationExecutor executor = new(
                signer,
                directoryService,
                Substitute.For<ILogger<ISigner>>(),
                coordinator);
            bool laterOwnerRan = false;

            await executor.ExecuteAsync(plan, options);
            await coordinator.ExecuteAsync(
                plan.Source.SourceIdentity,
                () =>
                {
                    laterOwnerRan = true;

                    return Task.FromResult(source);
                });

            Assert.Equal(
                "unsigned",
                File.ReadAllText(plan.Output.FullName));
            Assert.True(laterOwnerRan);
            await signer
                .DidNotReceiveWithAnyArgs()
                .SignOwnerAsync(default!, default!, default!);
        }

        [Fact]
        public async Task ExecuteAsync_DuplicateSourceFailure_SharesFailure()
        {
            using TestDirectory sourceDirectory = new();
            using TestDirectory outputDirectory = new();
            using DirectoryServiceStub directoryService = new();
            await using SigningOperationCoordinator coordinator =
                new(directoryService);
            FileInfo source = sourceDirectory.CreateFile(
                relativePath: "source.bin",
                contents: "unsigned");
            SigningFile signingFile = SigningFile.Capture(source);
            SigningOperationPlan first = new(
                signingFile,
                new FileInfo(
                    Path.Combine(
                        outputDirectory.FullPath,
                        "first.bin")));
            SigningOperationPlan second = new(
                signingFile,
                new FileInfo(
                    Path.Combine(
                        outputDirectory.FullPath,
                        "second.bin")));
            SignOptions options = new(
                HashAlgorithmName.SHA256,
                new Uri("https://timestamp.test"));
            IAggregatingDataFormatSigner signer =
                Substitute.For<IAggregatingDataFormatSigner>();
            InvalidOperationException expected =
                new(message: "Failure.");

            signer
                .HasSigningWork(
                    Arg.Any<FileInfo>(),
                    options)
                .Returns(true);
            signer
                .SignOwnerAsync(
                    Arg.Any<SigningFile>(),
                    options,
                    coordinator)
                .Returns(Task.FromException(expected));
            SigningOperationExecutor executor = new(
                signer,
                directoryService,
                Substitute.For<ILogger<ISigner>>(),
                coordinator);
            Task firstTask = executor.ExecuteAsync(first, options);
            Task secondTask = executor.ExecuteAsync(second, options);

            InvalidOperationException firstException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => firstTask);
            InvalidOperationException secondException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => secondTask);

            Assert.Same(expected, firstException);
            Assert.Same(expected, secondException);
            Assert.False(first.Output.Exists);
            Assert.False(second.Output.Exists);
        }
    }
}
