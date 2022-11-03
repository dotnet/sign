namespace Sign.Core.Test
{
    public class TemporaryDirectoryTests
    {
        [Fact]
        public void Constructor_WhenDirectoryIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new TemporaryDirectory(directoryService: null!));

            Assert.Equal("directoryService", exception.ParamName);
        }

        [Fact]
        public void Constructor_Always_CreatesDirectory()
        {
            using (DirectoryServiceSpy spy = new())
            {
                Assert.False(spy.WasCreated);

                using (TemporaryDirectory directory = new(spy))
                {
                    Assert.True(spy.WasCreated);
                }
            }
        }

        [Fact]
        public void Directory_Always_ReturnsRootDirectory()
        {
            using (DirectoryServiceSpy spy = new())
            using (TemporaryDirectory directory = new(spy))
            {
                Assert.Equal(spy.Directory.FullName, directory.Directory.FullName);
            }
        }

        [Fact]
        public void Dispose_Always_DeletesDirectory()
        {
            using (DirectoryServiceSpy spy = new())
            {
                Assert.False(spy.WasDeleted);

                using (TemporaryDirectory directory = new(spy))
                {
                }

                Assert.True(spy.WasDeleted);
            }
        }

        private sealed class DirectoryServiceSpy : IDirectoryService
        {
            internal DirectoryInfo Directory { get; }

            internal bool WasCreated { get; private set; }
            internal bool WasDeleted { get; private set; }

            internal DirectoryServiceSpy()
            {
                Directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

                Directory.Create();
            }

            public DirectoryInfo CreateTemporaryDirectory()
            {
                WasCreated = true;

                return Directory;
            }

            public void Delete(DirectoryInfo directory)
            {
                WasDeleted = true;

                directory.Delete(recursive: true);
            }

            public void Dispose()
            {
            }
        }
    }
}