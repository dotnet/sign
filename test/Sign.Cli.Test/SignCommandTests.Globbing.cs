// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sign.Core;

namespace Sign.Cli.Test
{
    public partial class SignCommandTests
    {
        public sealed class GlobbingTests : IDisposable
        {
            private readonly DirectoryService _directoryService;
            private readonly SignCommand _signCommand;
            private readonly SignerSpy _signerSpy;
            private readonly TemporaryDirectory _temporaryDirectory;

            public GlobbingTests()
            {
                ServiceCollection services = new();

                _signerSpy = new SignerSpy();

                services.AddSingleton<IMatcherFactory, MatcherFactory>();
                services.AddSingleton<IFileListReader, FileListReader>();
                services.AddSingleton<IFileMatcher, FileMatcher>();
                services.AddSingleton<ISigner>(_signerSpy);

                TestServiceProviderFactory serviceProviderFactory = new(services.BuildServiceProvider());

                _signCommand = Program.CreateCommand(serviceProviderFactory);

                _directoryService = new DirectoryService(Mock.Of<ILogger<IDirectoryService>>());
                _temporaryDirectory = new TemporaryDirectory(_directoryService);

                CreateFileSystemInfos(
                    _temporaryDirectory,
                    "a.dll",
                    "b.DLL",
                    "c.exe",
                    "d.EXE",
                    "dll",
                    "exe",
                    "e/f.dll",
                    "e/g.DLL",
                    "e/h.exe",
                    "e/i.EXE");
            }

            public void Dispose()
            {
                _temporaryDirectory.Dispose();
                _directoryService.Dispose();

                GC.SuppressFinalize(this);
            }

            [Fact]
            public async Task Command_WhenFileIsGlobPattern_SignsOnlyMatches()
            {
                string commandText = $"code --description {Description} --description-url {DescriptionUrl} --timestamp-url {TimestampUrl} "
                    + $"-b \"{_temporaryDirectory.Directory.FullName}\" azure-key-vault -kvu {KeyVaultUrl} -kvc {CertificateName} **/*.dll";

                int exitCode = await _signCommand.Parse(commandText).InvokeAsync();

                Assert.Equal(_signerSpy.ExitCode, exitCode);
                Assert.NotNull(_signerSpy.InputFiles);
                Assert.Collection(_signerSpy.InputFiles,
                    inputFile => AssertIsExpectedInputFile(inputFile, _temporaryDirectory, "a.dll"),
                    inputFile => AssertIsExpectedInputFile(inputFile, _temporaryDirectory, "b.DLL"),
                    inputFile => AssertIsExpectedInputFile(inputFile, _temporaryDirectory, @"e\f.dll"),
                    inputFile => AssertIsExpectedInputFile(inputFile, _temporaryDirectory, @"e\g.DLL"));
            }

            [Fact]
            public async Task Command_WhenFileIsGlobPatternWithSubdirectory_SignsOnlyMatches()
            {
                string commandText = $"code --description {Description} --description-url {DescriptionUrl} --timestamp-url {TimestampUrl} "
                    + $"-b \"{_temporaryDirectory.Directory.FullName}\" azure-key-vault -kvu {KeyVaultUrl} -kvc {CertificateName} **/e/*.dll";

                int exitCode = await _signCommand.Parse(commandText).InvokeAsync();

                Assert.Equal(_signerSpy.ExitCode, exitCode);
                Assert.NotNull(_signerSpy.InputFiles);
                Assert.Collection(_signerSpy.InputFiles,
                    inputFile => AssertIsExpectedInputFile(inputFile, _temporaryDirectory, @"e\f.dll"),
                    inputFile => AssertIsExpectedInputFile(inputFile, _temporaryDirectory, @"e\g.DLL"));
            }

            [Fact]
            public async Task Command_WhenFileIsGlobPatternWithBracedExpansion_SignsOnlyMatches()
            {
                string commandText = $"code --description {Description} --description-url {DescriptionUrl} --timestamp-url {TimestampUrl} "
                      + $"-b \"{_temporaryDirectory.Directory.FullName}\" azure-key-vault -kvu {KeyVaultUrl} -kvc {CertificateName} **/*.{{dll,exe}}";

                int exitCode = await _signCommand.Parse(commandText).InvokeAsync();

                Assert.Equal(_signerSpy.ExitCode, exitCode);
                Assert.NotNull(_signerSpy.InputFiles);
                Assert.Collection(_signerSpy.InputFiles,
                    inputFile => AssertIsExpectedInputFile(inputFile, _temporaryDirectory, "a.dll"),
                    inputFile => AssertIsExpectedInputFile(inputFile, _temporaryDirectory, "b.DLL"),
                    inputFile => AssertIsExpectedInputFile(inputFile, _temporaryDirectory, "c.exe"),
                    inputFile => AssertIsExpectedInputFile(inputFile, _temporaryDirectory, "d.EXE"),
                    inputFile => AssertIsExpectedInputFile(inputFile, _temporaryDirectory, @"e\f.dll"),
                    inputFile => AssertIsExpectedInputFile(inputFile, _temporaryDirectory, @"e\g.DLL"),
                    inputFile => AssertIsExpectedInputFile(inputFile, _temporaryDirectory, @"e\h.exe"),
                    inputFile => AssertIsExpectedInputFile(inputFile, _temporaryDirectory, @"e\i.EXE"));
            }

            private static void AssertIsExpectedInputFile(FileInfo inputFile, TemporaryDirectory temporaryDirectory, string relativePath)
            {
                string expectedFilePath = Path.Combine(temporaryDirectory.Directory.FullName, relativePath);
                string actualFilePath = inputFile.FullName;

                Assert.Equal(expectedFilePath, actualFilePath);
            }

            private static void CreateFileSystemInfos(TemporaryDirectory directory, params string[] paths)
            {
                foreach (string path in paths)
                {
                    bool isDirectory = path.EndsWith('/');
                    string relativePath = path.TrimEnd('/').Replace('/', '\\');
                    string fullPath = Path.Combine(directory.Directory.FullName, relativePath);

                    if (isDirectory)
                    {
                        CreateSubdirectory(fullPath);
                    }
                    else
                    {
                        CreateFile(fullPath);
                    }
                }
            }

            private static void CreateSubdirectory(string fullPath)
            {
                DirectoryInfo subdirectory = new(fullPath);

                EnsureParentDirectoriesExist(subdirectory.Parent!);

                subdirectory.Create();
            }

            private static void CreateFile(string fullPath)
            {
                FileInfo file = new(fullPath);

                EnsureParentDirectoriesExist(file.Directory!);

                System.IO.File.WriteAllText(file.FullName, string.Empty);
            }

            private static void EnsureParentDirectoriesExist(DirectoryInfo directory)
            {
                Stack<DirectoryInfo> directoriesToCreate = new();
                DirectoryInfo? parent = directory;

                while (parent is not null)
                {
                    if (parent.Exists)
                    {
                        break;
                    }

                    directoriesToCreate.Push(parent);

                    parent = parent.Parent;
                }

                while (directoriesToCreate.TryPop(out DirectoryInfo? directoryToCreate))
                {
                    directoryToCreate.Create();
                }
            }
        }
    }
}
