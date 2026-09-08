// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Text;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WixToolset.Dtf.Compression.Cab;

namespace Sign.Core.Test
{
    public class CabContainerTests
    {
        [Fact]
        public void Constructor_WhenCabFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new CabContainer(
                    cabFile: null!,
                    Substitute.For<IDirectoryService>(),
                    Substitute.For<IFileMatcher>(),
                    Substitute.For<ILogger>()));

            Assert.Equal("cabFile", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenDirectoryServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new CabContainer(
                    new FileInfo("a"),
                    directoryService: null!,
                    Substitute.For<IFileMatcher>(),
                    Substitute.For<ILogger>()));

            Assert.Equal("directoryService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenFileMatcherIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new CabContainer(
                    new FileInfo("a"),
                    Substitute.For<IDirectoryService>(),
                    fileMatcher: null!,
                    Substitute.For<ILogger>()));

            Assert.Equal("fileMatcher", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new CabContainer(
                    new FileInfo("a"),
                    Substitute.For<IDirectoryService>(),
                    Substitute.For<IFileMatcher>(),
                    logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public async Task OpenAsync_WhenCabFileIsNonEmpty_ExtractsCabToDirectory()
        {
            string[] expectedFileNames = [".a", "b", "c.d"];
            FileInfo cabFile = CreateCabFile(expectedFileNames);

            using (DirectoryServiceStub directoryService = new())
            using (CabContainer container = new(cabFile, directoryService, Substitute.For<IFileMatcher>(), Substitute.For<ILogger>()))
            {
                await container.OpenAsync();

                FileInfo[] actualFiles = directoryService.Directories[0].GetFiles("*", SearchOption.AllDirectories);
                string[] actualFileNames = actualFiles
                    .Select(file => file.FullName.Substring(directoryService.Directories[0].FullName.Length + 1))
                    .ToArray();

                Assert.Equal(expectedFileNames, actualFileNames);
            }
        }

        [Fact]
        public async Task SaveAsync_WhenCabFileIsNonEmpty_CompressesCabFromDirectory()
        {
            string[] expectedFileNames = ["a", "b"];
            FileInfo cabFile = CreateCabFile(expectedFileNames);

            using (DirectoryServiceStub directoryService = new())
            using (CabContainer container = new(cabFile, directoryService, Substitute.For<IFileMatcher>(), Substitute.For<ILogger>()))
            {
                await container.OpenAsync();

                File.WriteAllText(Path.Combine(directoryService.Directories[0].FullName, "b"), "updated");

                await container.SaveAsync();
            }

            using (DirectoryServiceStub directoryService = new())
            using (CabContainer container = new(cabFile, directoryService, Substitute.For<IFileMatcher>(), Substitute.For<ILogger>()))
            {
                await container.OpenAsync();

                CabInfo cab = new(cabFile.FullName);
                string[] actualFileNames = cab.GetFiles().Select(file => file.Name).ToArray();

                Assert.Equal(expectedFileNames, actualFileNames);
                Assert.Equal("updated", File.ReadAllText(Path.Combine(directoryService.Directories[0].FullName, "b")));
            }
        }

        [Fact]
        public async Task SaveAsync_WhenCabFileIsNonEmpty_PreservesEntryOrder()
        {
            // Windows Installer requires the order of the files in a cabinet to match the order of
            // the File table's Sequence column.  See https://learn.microsoft.com/windows/win32/msi/cabinet-files.
            string[] expectedFileNames = ["c", "a", "b"];
            FileInfo cabFile = CreateCabFile(expectedFileNames);

            using (DirectoryServiceStub directoryService = new())
            using (CabContainer container = new(cabFile, directoryService, Substitute.For<IFileMatcher>(), Substitute.For<ILogger>()))
            {
                await container.OpenAsync();
                await container.SaveAsync();
            }

            CabInfo cab = new(cabFile.FullName);
            string[] actualFileNames = cab.GetFiles().Select(file => file.Name).ToArray();

            Assert.Equal(expectedFileNames, actualFileNames);
        }

        private static FileInfo CreateCabFile(params string[] entryNames)
        {
            FileInfo file = new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

            CabInfo cab = new(file.FullName);

            List<string> sourceFiles = new();

            foreach (string entryName in entryNames)
            {
                string sourceFile = Path.GetTempFileName();

                File.WriteAllBytes(sourceFile, Encoding.UTF8.GetBytes(entryName));

                sourceFiles.Add(sourceFile);
            }

            cab.PackFiles(sourceDirectory: null, sourceFiles, entryNames);

            return file;
        }
    }
}
