// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Text;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WixToolset.Dtf.Compression.Cab;
using WixToolset.Dtf.WindowsInstaller;

namespace Sign.Core.Test
{
    public class MsiContainerTests
    {
        [Fact]
        public void Constructor_WhenMsiFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new MsiContainer(
                    msiFile: null!,
                    Substitute.For<IDirectoryService>(),
                    Substitute.For<IFileMatcher>(),
                    Substitute.For<ILogger>()));

            Assert.Equal("msiFile", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenDirectoryServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new MsiContainer(
                    new FileInfo("a"),
                    directoryService: null!,
                    Substitute.For<IFileMatcher>(),
                    Substitute.For<ILogger>()));

            Assert.Equal("directoryService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new MsiContainer(
                    new FileInfo("a"),
                    Substitute.For<IDirectoryService>(),
                    Substitute.For<IFileMatcher>(),
                    logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task OpenAsync_ExtractsPackagedFiles(bool embedCabinet)
        {
            using (DirectoryServiceStub directoryService = new())
            {
                DirectoryInfo directory = directoryService.CreateTemporaryDirectory();
                FileInfo msiFile = MsiCreator.Create(
                    directory,
                    [CreateFile(directory, "a.txt", "a"), CreateFile(directory, "b.txt", "b")],
                    embedCabinet);

                using (MsiContainer container = CreateContainer(msiFile, directoryService))
                {
                    await container.OpenAsync();

                    string[] actualFileNames = container.GetFiles()
                        .Select(file => file.Name)
                        .OrderBy(name => name)
                        .ToArray();

                    Assert.Equal(["a.txt", "b.txt"], actualFileNames);
                }
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SaveAsync_WhenFileChanged_UpdatesPackagedFile(bool embedCabinet)
        {
            using (DirectoryServiceStub directoryService = new())
            {
                DirectoryInfo directory = directoryService.CreateTemporaryDirectory();
                FileInfo msiFile = MsiCreator.Create(
                    directory,
                    [CreateFile(directory, "a.txt", "a"), CreateFile(directory, "b.txt", "b")],
                    embedCabinet);

                await ChangeAndSaveAsync(msiFile, directoryService, "b.txt", "updated");

                // Reopen the package to see what was actually packaged.
                using (MsiContainer container = CreateContainer(msiFile, directoryService))
                {
                    await container.OpenAsync();

                    FileInfo actualFile = container.GetFiles().Single(file => file.Name == "b.txt");

                    Assert.Equal("updated", File.ReadAllText(actualFile.FullName));
                }
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SaveAsync_WhenFileChanged_UpdatesFileTable(bool embedCabinet)
        {
            using (DirectoryServiceStub directoryService = new())
            {
                DirectoryInfo directory = directoryService.CreateTemporaryDirectory();
                FileInfo msiFile = MsiCreator.Create(
                    directory,
                    [CreateFile(directory, "a.txt", "a")],
                    embedCabinet);

                await ChangeAndSaveAsync(msiFile, directoryService, "a.txt", "much longer contents");

                using (Database database = new(msiFile.FullName, DatabaseOpenMode.ReadOnly))
                {
                    int actualFileSize = database.ExecuteIntegerQuery(
                        "SELECT `FileSize` FROM `File` WHERE `File` = 'File0'").Single();

                    Assert.Equal("much longer contents".Length, actualFileSize);
                }
            }
        }

        [Fact]
        public async Task SaveAsync_WhenCabinetIsExternal_RebuildsCabinetBesideThePackage()
        {
            using (DirectoryServiceStub directoryService = new())
            {
                DirectoryInfo directory = directoryService.CreateTemporaryDirectory();
                FileInfo msiFile = MsiCreator.Create(
                    directory,
                    [CreateFile(directory, "a.txt", "a"), CreateFile(directory, "b.txt", "b")],
                    embedCabinet: false);
                FileInfo cabFile = new(Path.Combine(directory.FullName, "test.cab"));

                await ChangeAndSaveAsync(msiFile, directoryService, "b.txt", "updated");

                Assert.True(cabFile.Exists);

                using (DirectoryServiceStub unpackDirectoryService = new())
                {
                    DirectoryInfo unpackDirectory = unpackDirectoryService.CreateTemporaryDirectory();
                    CabInfo cab = new(cabFile.FullName);

                    // The entries must stay in File table sequence order.
                    Assert.Equal(["File0", "File1"], cab.GetFiles().Select(file => file.Name));

                    cab.Unpack(unpackDirectory.FullName);

                    Assert.Equal("updated", File.ReadAllText(Path.Combine(unpackDirectory.FullName, "File1")));
                }
            }
        }

        [Fact]
        public async Task OpenAsync_WhenCabinetIsExternalAndMissing_Throws()
        {
            using (DirectoryServiceStub directoryService = new())
            {
                DirectoryInfo directory = directoryService.CreateTemporaryDirectory();
                FileInfo msiFile = MsiCreator.Create(
                    directory,
                    [CreateFile(directory, "a.txt", "a")],
                    embedCabinet: false);

                File.Delete(Path.Combine(directory.FullName, "test.cab"));

                using (MsiContainer container = CreateContainer(msiFile, directoryService))
                {
                    await Assert.ThrowsAnyAsync<Exception>(async () => await container.OpenAsync());
                }
            }
        }

        private static async Task ChangeAndSaveAsync(
            FileInfo msiFile,
            DirectoryServiceStub directoryService,
            string fileName,
            string contents)
        {
            using (MsiContainer container = CreateContainer(msiFile, directoryService))
            {
                await container.OpenAsync();

                FileInfo file = container.GetFiles().Single(f => f.Name == fileName);

                File.WriteAllText(file.FullName, contents);

                await container.SaveAsync();
            }
        }

        private static MsiContainer CreateContainer(FileInfo msiFile, IDirectoryService directoryService)
        {
            return new MsiContainer(
                msiFile,
                directoryService,
                Substitute.For<IFileMatcher>(),
                Substitute.For<ILogger>());
        }

        private static FileInfo CreateFile(DirectoryInfo directory, string name, string contents)
        {
            FileInfo file = new(Path.Combine(directory.FullName, name));

            File.WriteAllBytes(file.FullName, Encoding.UTF8.GetBytes(contents));

            return file;
        }
    }
}
