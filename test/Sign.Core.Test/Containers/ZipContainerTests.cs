using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;

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
                    Mock.Of<IDirectoryService>(),
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<ILogger>()));

            Assert.Equal("zipFile", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenDirectoryServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ZipContainer(
                    new FileInfo("a"),
                    directoryService: null!,
                    Mock.Of<IFileMatcher>(),
                    Mock.Of<ILogger>()));

            Assert.Equal("directoryService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenFileMatcherIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ZipContainer(
                    new FileInfo("a"),
                    Mock.Of<IDirectoryService>(),
                    fileMatcher: null!,
                    Mock.Of<ILogger>()));

            Assert.Equal("fileMatcher", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ZipContainer(
                    new FileInfo("a"),
                    Mock.Of<IDirectoryService>(),
                    Mock.Of<IFileMatcher>(),
                    logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public async Task OpenAsync_WhenZipFileIsNonEmpty_ExtractsZipToDirectory()
        {
            string[] expectedFileNames = new[] { ".a", "b", "c.d" };
            FileInfo zipFile = CreateZipFile(expectedFileNames);

            using (DirectoryServiceStub directoryService = new())
            using (ZipContainer container = new(zipFile, directoryService, Mock.Of<IFileMatcher>(), Mock.Of<ILogger>()))
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
            using (ZipContainer container = new(zipFile, directoryService, Mock.Of<IFileMatcher>(), Mock.Of<ILogger>()))
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

        private sealed class DirectoryServiceStub : IDirectoryService
        {
            private readonly List<DirectoryInfo> _directories;

            internal IReadOnlyList<DirectoryInfo> Directories { get; }

            internal DirectoryServiceStub()
            {
                Directories = _directories = new List<DirectoryInfo>();
            }

            public DirectoryInfo CreateTemporaryDirectory()
            {
                DirectoryInfo directory = new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

                directory.Create();

                _directories.Add(directory);

                return directory;
            }

            public void Delete(DirectoryInfo directory)
            {
                directory.Refresh();

                if (directory.Exists)
                {
                    directory.Delete(recursive: true);
                }
            }

            public void Dispose()
            {
                foreach (DirectoryInfo directory in _directories)
                {
                    Delete(directory);
                }
            }
        }
    }
}