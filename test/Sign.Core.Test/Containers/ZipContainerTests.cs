// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Sign.Core.Test
{
    public class ZipContainerTests
    {
        [Fact]
        public void Constructor_WhenZipFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ZipContainer(
                    zipFile: null!,
                    Substitute.For<IDirectoryService>(),
                    Substitute.For<IFileMatcher>(),
                    Substitute.For<ILogger>()));

            Assert.Equal("zipFile", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenDirectoryServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ZipContainer(
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
                () => new ZipContainer(
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
                () => new ZipContainer(
                    new FileInfo("a"),
                    Substitute.For<IDirectoryService>(),
                    Substitute.For<IFileMatcher>(),
                    logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public async Task Dispose_WhenOpened_RemovesTemporaryDirectory()
        {
            string[] expectedFileNames = new[] { "a" };
            FileInfo zipFile = CreateZipFile(expectedFileNames);

            using (DirectoryServiceStub directoryService = new())
            {
                DirectoryInfo? directory;

                using (ZipContainer container = new(zipFile, directoryService, Substitute.For<IFileMatcher>(), Substitute.For<ILogger>()))
                {
                    await container.OpenAsync();

                    directory = Assert.Single(directoryService.Directories);

                    Assert.True(directory.Exists);
                }

                directory = Assert.Single(directoryService.Directories);

                Assert.False(directory.Exists);
            }
        }

        [Fact]
        public async Task OpenAsync_WhenZipFileIsNonEmpty_ExtractsZipToDirectory()
        {
            string[] expectedFileNames = new[] { ".a", "b", "c.d" };
            FileInfo zipFile = CreateZipFile(expectedFileNames);

            using (DirectoryServiceStub directoryService = new())
            using (ZipContainer container = new(zipFile, directoryService, Substitute.For<IFileMatcher>(), Substitute.For<ILogger>()))
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
        public async Task SaveAsync_WhenZipFileIsNonEmpty_CompressesZipFromDirectory()
        {
            string[] fileNames = new[] { "a" };
            FileInfo zipFile = CreateZipFile(fileNames);

            using (DirectoryServiceStub directoryService = new())
            using (ZipContainer container = new(zipFile, directoryService, Substitute.For<IFileMatcher>(), Substitute.For<ILogger>()))
            {
                await container.OpenAsync();

                File.WriteAllText(Path.Combine(directoryService.Directories[0].FullName, "b"), "b");

                await container.SaveAsync();
            }

            using (FileStream stream = zipFile.OpenRead())
            using (ZipArchive zip = new(stream, ZipArchiveMode.Read))
            {
                Assert.Equal(2, zip.Entries.Count);
                Assert.NotNull(zip.GetEntry("a"));
                Assert.NotNull(zip.GetEntry("b"));
            }
        }

        [Fact]
        public async Task GetFiles_WithParentIdentity_ReturnsEntriesRelativeToExtractionRoot()
        {
            FileInfo zipFile = CreateZipFile("lib/a.dll", "readme.txt");
            SigningSourceIdentity parentIdentity =
                SigningSourceIdentity.Capture(zipFile);
            Matcher matcher = new();

            matcher.AddInclude("**/*.dll");

            using (DirectoryServiceStub directoryService = new())
            using (ZipContainer container = new(zipFile, directoryService, new FileMatcher(), Substitute.For<ILogger>()))
            {
                await container.OpenAsync();

                string extractionRoot = directoryService.Directories[0].FullName;
                SigningFile[] allFiles = container
                    .GetFiles(parentIdentity)
                    .OrderBy(file => file.File.FullName, StringComparer.Ordinal)
                    .ToArray();
                SigningFile matchedFile = Assert.Single(
                    container.GetFiles(matcher, parentIdentity));

                Assert.Collection(
                    allFiles,
                    file => AssertEntry(file, extractionRoot, parentIdentity, "lib/a.dll"),
                    file => AssertEntry(file, extractionRoot, parentIdentity, "readme.txt"));
                AssertEntry(matchedFile, extractionRoot, parentIdentity, "lib/a.dll");
            }
        }

        private static void AssertEntry(
            SigningFile file,
            string extractionRoot,
            SigningSourceIdentity parentIdentity,
            string entryPath)
        {
            Assert.Equal(
                Path.GetFullPath(Path.Combine(extractionRoot, entryPath)),
                file.File.FullName);
            Assert.Equal(
                SigningSourceIdentity.ContainerEntry(parentIdentity, entryPath),
                file.SourceIdentity);
        }

        private static FileInfo CreateZipFile(params string[] entryNames)
        {
            FileInfo file = new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

            using (FileStream stream = file.OpenWrite())
            using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
            {
                foreach (string entryName in entryNames)
                {
                    ZipArchiveEntry entry = zip.CreateEntry(entryName);

                    using (Stream entryStream = entry.Open())
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(entryName);

                        entryStream.Write(bytes);
                    }
                }
            }

            return file;
        }
    }
}