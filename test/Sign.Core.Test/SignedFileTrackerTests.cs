// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    public sealed class SignedFileTrackerTests : IDisposable
    {
        private readonly DirectoryService _directoryService;
        private readonly ISignedFileTracker _tracker;
        private readonly TemporaryDirectory _directory;

        public SignedFileTrackerTests()
        {
            _directoryService = new DirectoryService(Mock.Of<ILogger<IDirectoryService>>());
            _tracker = new SignedFileTracker();
            _directory = new TemporaryDirectory(_directoryService);
        }

        public void Dispose()
        {
            _directory.Dispose();
            _directoryService.Dispose();
        }

        [Fact]
        public void Constructor_Always_InitializesEmptyTracker()
        {
            FileInfo file = new(Path.Combine(_directory.Directory.FullName, "test.dll"));
            File.WriteAllText(file.FullName, "test content");

            Assert.False(_tracker.HasSigned(file));
        }

        [Fact]
        public void IsFileSigned_WhenFileIsNull_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _tracker.HasSigned(null!));
        }

        [Fact]
        public void IsFileSigned_WhenFileNotYetSigned_ReturnsFalse()
        {
            FileInfo file = new(Path.Combine(_directory.Directory.FullName, "test.dll"));
            File.WriteAllText(file.FullName, "test content");

            Assert.False(_tracker.HasSigned(file));
        }

        [Fact]
        public void IsFileSigned_AfterMarkAsSigned_ReturnsTrue()
        {
            FileInfo file = new(Path.Combine(_directory.Directory.FullName, "test.dll"));
            File.WriteAllText(file.FullName, "test content");

            _tracker.MarkAsSigned(file);

            Assert.True(_tracker.HasSigned(file));
        }

        [Fact]
        public void IsFileSigned_WithDifferentPathRepresentations_UsesCanonicalPath()
        {
            string fileName = "test.dll";
            string fullPath = Path.Combine(_directory.Directory.FullName, fileName);
            File.WriteAllText(fullPath, "test content");

            FileInfo file1 = new(fullPath);
            _tracker.MarkAsSigned(file1);

            string pathWithTraversal = Path.Combine(_directory.Directory.FullName, "subfolder", "..", fileName);
            FileInfo file2 = new(pathWithTraversal);

            Assert.True(_tracker.HasSigned(file2));
        }

        [Fact]
        public void MarkAsSigned_WhenFileIsNull_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _tracker.MarkAsSigned(null!));
        }

        [Fact]
        public void MarkAsSigned_WithRelativePath_UsesCanonicalPath()
        {
            string fileName = "test.dll";
            string fullPath = Path.Combine(_directory.Directory.FullName, fileName);
            File.WriteAllText(fullPath, "test content");

            string originalDirectory = Directory.GetCurrentDirectory();

            try
            {
                Directory.SetCurrentDirectory(_directory.Directory.FullName);

                FileInfo file1 = new(fileName);
                _tracker.MarkAsSigned(file1);

                FileInfo file2 = new(fullPath);
                Assert.True(_tracker.HasSigned(file2));
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDirectory);
            }
        }

        [Fact]
        public void MarkAsSigned_MultipleDifferentFiles_TracksIndependently()
        {
            FileInfo file1 = new(Path.Combine(_directory.Directory.FullName, "file1.dll"));
            FileInfo file2 = new(Path.Combine(_directory.Directory.FullName, "file2.dll"));
            FileInfo file3 = new(Path.Combine(_directory.Directory.FullName, "file3.dll"));

            File.WriteAllText(file1.FullName, "test content 1");
            File.WriteAllText(file2.FullName, "test content 2");
            File.WriteAllText(file3.FullName, "test content 3");

            _tracker.MarkAsSigned(file1);
            _tracker.MarkAsSigned(file3);

            Assert.True(_tracker.HasSigned(file1));
            Assert.False(_tracker.HasSigned(file2));
            Assert.True(_tracker.HasSigned(file3));
        }

        [Fact]
        public void IsFileSigned_ThreadSafe_MultipleThreads()
        {
            List<FileInfo> files = new();

            for (int i = 0; i < 100; i++)
            {
                FileInfo file = new(Path.Combine(_directory.Directory.FullName, $"file{i}.dll"));
                File.WriteAllText(file.FullName, $"test content {i}");
                files.Add(file);
            }

            Parallel.ForEach(files, file =>
            {
                _tracker.MarkAsSigned(file);
            });

            foreach (FileInfo file in files)
            {
                Assert.True(_tracker.HasSigned(file));
            }
        }

        [Fact]
        public void MarkAsSigned_ConcurrentAccess_NoDuplicates()
        {
            FileInfo file = new(Path.Combine(_directory.Directory.FullName, "test.dll"));
            File.WriteAllText(file.FullName, "test content");

            int threadCount = 10;

            Parallel.For(0, threadCount, i =>
            {
                _tracker.MarkAsSigned(file);
            });

            Assert.True(_tracker.HasSigned(file));
        }

        [Theory]
        [InlineData("TEST.DLL", "test.dll")]
        [InlineData("Test.DLL", "test.dll")]
        [InlineData("TEST.dll", "test.dll")]
        public void IsFileSigned_OnWindows_IsCaseInsensitive(string fileName1, string fileName2)
        {
            Assert.True(OperatingSystem.IsWindows(), "This test is only valid on Windows.");

            string fullPath = Path.Combine(_directory.Directory.FullName, fileName1);
            File.WriteAllText(fullPath, "test content");

            FileInfo file1 = new(fullPath);
            _tracker.MarkAsSigned(file1);

            FileInfo file2 = new(Path.Combine(_directory.Directory.FullName, fileName2));

            Assert.True(_tracker.HasSigned(file2));
        }
    }
}
