namespace Sign.Core.Test
{
    public partial class AggregatingSignatureProviderTests
    {
        private static readonly string AppxBundleContainerName = "container.appxbundle";
        private static readonly string AppxContainerName = "container.appx";
        private static readonly string ZipContainerName = "container.zip";

        [Fact]
        public async Task SignAsync_WhenFileIsEmptyAppxBundleContainer_SignsNothing()
        {
            AggregatingSignatureProviderTest test = new(AppxBundleContainerName);

            await test.Provider.SignAsync(test.Files, _options);

            ContainerSpy container = test.Containers[AppxBundleContainerName];

            Assert.Equal(1, container.OpenAsync_CallCount);
            Assert.Equal(0, container.GetFiles_CallCount);
            Assert.Equal(1, container.GetFilesWithMatcher_CallCount);
            Assert.Equal(0, container.SaveAsync_CallCount);
            Assert.Equal(1, container.Dispose_CallCount);

            Assert.Collection(
                test.SignatureProviderSpy.SignedFiles,
                signedFile => Assert.Equal(AppxBundleContainerName, signedFile.Name));
        }

        [Fact]
        public async Task SignAsync_WhenFileIsAppxBundleContainer_SignsNestedAppxAndMsixFiles()
        {
            AggregatingSignatureProviderTest test = new(
                $"{AppxBundleContainerName}/nestedcontainer.appx/a.dll",
                $"{AppxBundleContainerName}/nestedcontainer.msix/b.dll");

            await test.Provider.SignAsync(test.Files, _options);

            ContainerSpy container = test.Containers[AppxBundleContainerName];

            Assert.Equal(1, container.OpenAsync_CallCount);
            Assert.Equal(0, container.GetFiles_CallCount);
            Assert.Equal(1, container.GetFilesWithMatcher_CallCount);
            Assert.Equal(1, container.SaveAsync_CallCount);
            Assert.Equal(1, container.Dispose_CallCount);

            Assert.Collection(
                test.SignatureProviderSpy.SignedFiles,
                signedFile => Assert.Equal("a.dll", signedFile.Name),
                signedFile => Assert.Equal("b.dll", signedFile.Name),
                signedFile => Assert.Equal("nestedcontainer.appx", signedFile.Name),
                signedFile => Assert.Equal("nestedcontainer.msix", signedFile.Name),
                signedFile => Assert.Equal(AppxBundleContainerName, signedFile.Name));
        }

        [Fact]
        public async Task SignAsync_WhenFileIsEmptyAppxContainer_SignsNothing()
        {
            AggregatingSignatureProviderTest test = new(AppxContainerName);

            await test.Provider.SignAsync(test.Files, _options);

            ContainerSpy container = test.Containers[AppxContainerName];

            Assert.Equal(1, container.OpenAsync_CallCount);
            Assert.Equal(1, container.GetFiles_CallCount);
            Assert.Equal(0, container.GetFilesWithMatcher_CallCount);
            Assert.Equal(1, container.SaveAsync_CallCount);
            Assert.Equal(1, container.Dispose_CallCount);

            Assert.Collection(
                test.SignatureProviderSpy.SignedFiles,
                signedFile => Assert.Equal(AppxContainerName, signedFile.Name));
        }

        [Fact]
        public async Task SignAsync_WhenFileIsAppxContainer_SignsPortableExecutableFiles()
        {
            AggregatingSignatureProviderTest test = new(
                $"{AppxContainerName}/a.dll",
                $"{AppxContainerName}/b.exe",
                $"{AppxContainerName}/c/d.dll");

            await test.Provider.SignAsync(test.Files, _options);

            ContainerSpy container = test.Containers[AppxContainerName];

            Assert.Equal(1, container.OpenAsync_CallCount);
            Assert.Equal(1, container.GetFiles_CallCount);
            Assert.Equal(0, container.GetFilesWithMatcher_CallCount);
            Assert.Equal(1, container.SaveAsync_CallCount);
            Assert.Equal(1, container.Dispose_CallCount);

            Assert.Collection(
                test.SignatureProviderSpy.SignedFiles,
                signedFile => Assert.Equal("a.dll", signedFile.Name),
                signedFile => Assert.Equal("b.exe", signedFile.Name),
                signedFile => Assert.Equal("d.dll", signedFile.Name),
                signedFile => Assert.Equal(AppxContainerName, signedFile.Name));
        }

        [Fact]
        public async Task SignAsync_WhenFileIsAppxContainerWithNestedContentAndContainers_SignsContentInsideOut()
        {
            AggregatingSignatureProviderTest test = new(
                $"{AppxContainerName}/a.dll",
                $"{AppxContainerName}/nestedcontainer0.zip/b.dll",
                $"{AppxContainerName}/nestedcontainer0.zip/nestedcontainer1.zip/c.dll",
                $"{AppxContainerName}/d.appinstaller",
                $"{AppxContainerName}/e.clickonce",
                $"{AppxContainerName}/nestedcontainer.nupkg/folder0/folder1/f.dll",
                $"{AppxContainerName}/nestedcontainer.vsix/folder0/folder1/folder2/g.dll");

            await test.Provider.SignAsync(test.Files, _options);

            foreach (string containerName in new[]
            {
                AppxContainerName,
                $"{AppxContainerName}/nestedcontainer0.zip",
                $"{AppxContainerName}/nestedcontainer0.zip/nestedcontainer1.zip",
                $"{AppxContainerName}/nestedcontainer.nupkg",
                $"{AppxContainerName}/nestedcontainer.vsix"
            })
            {
                ContainerSpy container = test.Containers[containerName];

                Assert.Equal(1, container.OpenAsync_CallCount);
                Assert.Equal(1, container.GetFiles_CallCount);
                Assert.Equal(0, container.GetFilesWithMatcher_CallCount);
                Assert.Equal(1, container.SaveAsync_CallCount);
                Assert.Equal(1, container.Dispose_CallCount);
            }

            Assert.Collection(
                test.SignatureProviderSpy.SignedFiles,
                signedFile => Assert.Equal("c.dll", signedFile.Name),
                signedFile => Assert.Equal("b.dll", signedFile.Name),
                signedFile => Assert.Equal("f.dll", signedFile.Name),
                signedFile => Assert.Equal("g.dll", signedFile.Name),
                signedFile => Assert.Equal("a.dll", signedFile.Name),
                signedFile => Assert.Equal("d.appinstaller", signedFile.Name),
                signedFile => Assert.Equal("e.clickonce", signedFile.Name),
                signedFile => Assert.Equal("nestedcontainer.nupkg", signedFile.Name),
                signedFile => Assert.Equal("nestedcontainer.vsix", signedFile.Name),
                signedFile => Assert.Equal("container.appx", signedFile.Name));
        }

        [Fact]
        public async Task SignAsync_WhenFileIsEmptyZipContainer_SignsNothing()
        {
            AggregatingSignatureProviderTest test = new(ZipContainerName);

            await test.Provider.SignAsync(test.Files, _options);

            ContainerSpy container = test.Containers[ZipContainerName];

            Assert.Equal(1, container.OpenAsync_CallCount);
            Assert.Equal(1, container.GetFiles_CallCount);
            Assert.Equal(0, container.GetFilesWithMatcher_CallCount);
            Assert.Equal(0, container.SaveAsync_CallCount);
            Assert.Equal(1, container.Dispose_CallCount);

            Assert.Empty(test.SignatureProviderSpy.SignedFiles);
        }

        [Fact]
        public async Task SignAsync_WhenFileIsZipContainer_SignsPortableExecutableFiles()
        {
            AggregatingSignatureProviderTest test = new(
                $"{ZipContainerName}/a.dll",
                $"{ZipContainerName}/b.exe",
                $"{ZipContainerName}/c/d.dll");

            await test.Provider.SignAsync(test.Files, _options);

            ContainerSpy container = test.Containers[ZipContainerName];

            Assert.Equal(1, container.OpenAsync_CallCount);
            Assert.Equal(1, container.GetFiles_CallCount);
            Assert.Equal(0, container.GetFilesWithMatcher_CallCount);
            Assert.Equal(1, container.SaveAsync_CallCount);
            Assert.Equal(1, container.Dispose_CallCount);

            Assert.Collection(
                test.SignatureProviderSpy.SignedFiles,
                signedFile => Assert.Equal("a.dll", signedFile.Name),
                signedFile => Assert.Equal("b.exe", signedFile.Name),
                signedFile => Assert.Equal("d.dll", signedFile.Name));
        }

        [Fact]
        public async Task SignAsync_WhenFileIsZipContainerWithNestedContentAndContainers_SignsContentInsideOut()
        {
            AggregatingSignatureProviderTest test = new(
                $"{ZipContainerName}/a.dll",
                $"{ZipContainerName}/nestedcontainer0.zip/b.dll",
                $"{ZipContainerName}/nestedcontainer0.zip/nestedcontainer1.zip/c.dll",
                $"{ZipContainerName}/d.appinstaller",
                $"{ZipContainerName}/e.clickonce",
                $"{ZipContainerName}/nestedcontainer.nupkg/folder0/folder1/f.dll",
                $"{ZipContainerName}/nestedcontainer.vsix/folder0/folder1/folder2/g.dll");

            await test.Provider.SignAsync(test.Files, _options);

            foreach (string containerName in new[]
            {
                ZipContainerName,
                $"{ZipContainerName}/nestedcontainer0.zip",
                $"{ZipContainerName}/nestedcontainer0.zip/nestedcontainer1.zip",
                $"{ZipContainerName}/nestedcontainer.nupkg",
                $"{ZipContainerName}/nestedcontainer.vsix"
            })
            {
                ContainerSpy container = test.Containers[containerName];

                Assert.Equal(1, container.OpenAsync_CallCount);
                Assert.Equal(1, container.GetFiles_CallCount);
                Assert.Equal(0, container.GetFilesWithMatcher_CallCount);
                Assert.Equal(1, container.SaveAsync_CallCount);
                Assert.Equal(1, container.Dispose_CallCount);
            }

            Assert.Collection(
                test.SignatureProviderSpy.SignedFiles,
                signedFile => Assert.Equal("c.dll", signedFile.Name),
                signedFile => Assert.Equal("b.dll", signedFile.Name),
                signedFile => Assert.Equal("f.dll", signedFile.Name),
                signedFile => Assert.Equal("g.dll", signedFile.Name),
                signedFile => Assert.Equal("a.dll", signedFile.Name),
                signedFile => Assert.Equal("d.appinstaller", signedFile.Name),
                signedFile => Assert.Equal("e.clickonce", signedFile.Name),
                signedFile => Assert.Equal("nestedcontainer.nupkg", signedFile.Name),
                signedFile => Assert.Equal("nestedcontainer.vsix", signedFile.Name));
        }
    }
}