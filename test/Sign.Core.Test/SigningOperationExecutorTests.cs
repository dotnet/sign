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
                        Assert.True(output.Directory!.Exists);
                        operations.Add("stage");
                    });
            signer
                .SignAsync(
                    Arg.Any<IEnumerable<SigningFile>>(),
                    options)
                .Returns(
                    call =>
                    {
                        SigningFile stagedFile = call
                            .Arg<IEnumerable<SigningFile>>()
                            .Single();

                        Assert.Equal(
                            plan.Source.SourceIdentity,
                            stagedFile.SourceIdentity);
                        File.AppendAllText(
                            stagedFile.File.FullName,
                            "-signed");
                        operations.Add("sign");

                        return Task.CompletedTask;
                    });
            signer
                .When(
                    value => value.CopySigningResults(
                        Arg.Any<FileInfo>(),
                        Arg.Is<DirectoryInfo>(
                            directory =>
                                directory.FullName ==
                                output.Directory!.FullName),
                        options))
                .Do(
                    call =>
                    {
                        File.WriteAllText(
                            output.FullName,
                            "stale-output");
                        operations.Add("publish");
                    });
            SigningOperationExecutor executor = new(
                signer,
                directoryService,
                Substitute.For<ILogger<ISigner>>());

            await executor.ExecuteAsync(plan, options);

            Assert.Equal(
                new[] { "stage", "sign", "publish" },
                operations);
            Assert.Equal(
                "unsigned-signed",
                File.ReadAllText(output.FullName));
        }

        [Fact]
        public void Stage_ArtifactRemainsAvailableUntilDisposed()
        {
            using TestDirectory sourceDirectory = new();
            using TestDirectory outputRoot = new();
            using DirectoryServiceStub directoryService = new();
            FileInfo source = new(
                Path.Combine(sourceDirectory.FullPath, "source.bin"));
            File.WriteAllText(source.FullName, "content");
            SigningOperationPlan plan = new(
                SigningFile.Capture(source),
                new FileInfo(
                    Path.Combine(outputRoot.FullPath, "output.bin")));
            SignOptions options = new(
                HashAlgorithmName.SHA256,
                new Uri("https://timestamp.test"));
            SigningOperationExecutor executor = new(
                Substitute.For<IAggregatingDataFormatSigner>(),
                directoryService,
                Substitute.For<ILogger<ISigner>>());

            SigningOperationStage stage = executor.Stage(plan, options);
            FileInfo stagedFile = stage.Input.File;
            DirectoryInfo stagingDirectory = stagedFile.Directory!;

            Assert.True(stagedFile.Exists);
            Assert.Equal(
                plan.Source.SourceIdentity,
                stage.Input.SourceIdentity);

            stage.Dispose();
            stagingDirectory.Refresh();

            Assert.False(stagingDirectory.Exists);
        }

        [Fact]
        public void StageAndPublish_WithClickOnceSigner_CopiesDependenciesBeforePrimaryFile()
        {
            using TestDirectory sourceDirectory = new();
            using TestDirectory outputRoot = new();
            using DirectoryServiceStub directoryService = new();
            string applicationFilesPath = Path.Combine(
                "Application Files",
                "MyApp_1_0_0_0");
            string dllPath = Path.Combine(
                applicationFilesPath,
                "MyApp.dll.deploy");
            string manifestPath = Path.Combine(
                applicationFilesPath,
                "MyApp.exe.manifest");
            FileInfo source = WriteFile(
                sourceDirectory.FullPath,
                "MyApp.application",
                "application");
            WriteFile(sourceDirectory.FullPath, "setup.exe", "setup");
            WriteFile(sourceDirectory.FullPath, dllPath, "dll");
            WriteFile(sourceDirectory.FullPath, manifestPath, "manifest");
            FileInfo output = new(
                Path.Combine(
                    outputRoot.FullPath,
                    "nested",
                    "Signed.application"));
            SigningOperationPlan plan = new(
                SigningFile.Capture(source),
                output);
            SignOptions options = new(
                HashAlgorithmName.SHA256,
                new Uri("https://timestamp.test"));
            IDataFormatSigner publishObserver =
                Substitute.For<IDataFormatSigner>();
            bool? dependencyPublishedBeforePrimary = null;

            publishObserver.CanSign(Arg.Any<FileInfo>()).Returns(true);
            publishObserver
                .When(
                    value => value.CopySigningResults(
                        Arg.Any<FileInfo>(),
                        Arg.Any<DirectoryInfo>(),
                        options))
                .Do(
                    call =>
                    {
                        dependencyPublishedBeforePrimary =
                            File.Exists(
                                Path.Combine(
                                    output.DirectoryName!,
                                    dllPath)) &&
                            !File.Exists(output.FullName);
                    });
            AggregatingSigner aggregatingSigner = new(
                [CreateClickOnceSigner(), publishObserver],
                Substitute.For<IDefaultDataFormatSigner>(),
                Substitute.For<IContainerProvider>(),
                Substitute.For<IFileMetadataService>(),
                Substitute.For<IMatcherFactory>());
            SigningOperationExecutor executor = new(
                aggregatingSigner,
                directoryService,
                Substitute.For<ILogger<ISigner>>());

            using (SigningOperationStage stage = executor.Stage(plan, options))
            {
                string stagingPath = stage.Input.File.DirectoryName!;

                Assert.Equal(
                    new[]
                    {
                        dllPath,
                        manifestPath,
                        "setup.exe",
                        stage.Input.File.Name
                    }.Order(StringComparer.Ordinal),
                    GetRelativeFilePaths(stagingPath));

                foreach (string path in new[]
                {
                    stage.Input.File.FullName,
                    Path.Combine(stagingPath, dllPath),
                    Path.Combine(stagingPath, manifestPath)
                })
                {
                    File.AppendAllText(path, "-signed");
                }

                executor.Publish(stage, plan, options);
            }

            Assert.True(dependencyPublishedBeforePrimary);
            Assert.Equal(
                new[]
                {
                    dllPath,
                    manifestPath,
                    "setup.exe",
                    output.Name
                }.Order(StringComparer.Ordinal),
                GetRelativeFilePaths(output.DirectoryName!));
            Assert.Equal(
                "application-signed",
                File.ReadAllText(output.FullName));
            Assert.Equal(
                "dll-signed",
                File.ReadAllText(
                    Path.Combine(output.DirectoryName!, dllPath)));
            Assert.Equal(
                "manifest-signed",
                File.ReadAllText(
                    Path.Combine(output.DirectoryName!, manifestPath)));
            Assert.Equal(
                "setup",
                File.ReadAllText(
                    Path.Combine(output.DirectoryName!, "setup.exe")));
        }

        private static ClickOnceSigner CreateClickOnceSigner()
        {
            return new ClickOnceSigner(
                Substitute.For<ISignatureAlgorithmProvider>(),
                Substitute.For<ICertificateProvider>(),
                Substitute.For<IServiceProvider>(),
                Substitute.For<IMageCli>(),
                Substitute.For<IManifestSigner>(),
                Substitute.For<ILogger<IDataFormatSigner>>(),
                Substitute.For<IFileMatcher>());
        }

        private static IEnumerable<string> GetRelativeFilePaths(
            string directoryPath)
        {
            return Directory
                .EnumerateFiles(
                    directoryPath,
                    "*",
                    SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(directoryPath, path))
                .Order(StringComparer.Ordinal);
        }

        private static FileInfo WriteFile(
            string directoryPath,
            string relativePath,
            string content)
        {
            FileInfo file = new(Path.Combine(directoryPath, relativePath));

            file.Directory!.Create();
            File.WriteAllText(file.FullName, content);

            return file;
        }
    }
}
