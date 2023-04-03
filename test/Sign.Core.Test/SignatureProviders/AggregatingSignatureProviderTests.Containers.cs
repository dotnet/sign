// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core.Test
{
    public partial class AggregatingSignatureProviderTests
    {
        private const string AppxBundleContainerName = "container.appxbundle";
        private const string AppxContainerName = "container.appx";
        private const string ZipContainerName = "container.zip";

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
        public async Task SignAsync_WhenFileIsAppxBundleContainerAndGlobAndAntiGlobPatternsAreUsed_SignsOnlyMatchingFiles()
        {
            const string fileListContents =
@"**/*.dll
**/*.exe
!**/*.txt
!**/DoNotSign/**/*";
            ReadFileList(fileListContents, out Matcher matcher, out Matcher antiMatcher);

            SignOptions options = new(
                applicationName: "a",
                publisherName: "b",
                description: "c",
                new Uri("https://description.test"),
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256,
                new Uri("https://timestamp.test"),
                matcher,
                antiMatcher);

            AggregatingSignatureProviderTest test = new(
                $"{AppxBundleContainerName}/a.dll",
                $"{AppxBundleContainerName}/b.DLL",
                $"{AppxBundleContainerName}/c.txt",
                $"{AppxBundleContainerName}/d.exe",
                $"{AppxBundleContainerName}/e.EXE",
                $"{AppxBundleContainerName}/f/g.dll",
                $"{AppxBundleContainerName}/f/h.txt",
                $"{AppxBundleContainerName}/f/i.exe",
                $"{AppxBundleContainerName}/DoNotSign/j.dll",
                $"{AppxBundleContainerName}/DoNotSign/k.txt",
                $"{AppxBundleContainerName}/DoNotSign/l/m.txt",
                $"{AppxBundleContainerName}/DoNotSign/l/n.exe");

            await test.Provider.SignAsync(test.Files, options);

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
        public async Task SignAsync_WhenFileIsAppxContainerAndGlobAndAntiGlobPatternsAreUsed_SignsOnlyMatchingFiles()
        {
            const string fileListContents =
@"**/*.dll
**/*.exe
!**/*.txt
!**/DoNotSign/**/*";
            ReadFileList(fileListContents, out Matcher matcher, out Matcher antiMatcher);

            SignOptions options = new(
                applicationName: "a",
                publisherName: "b",
                description: "c",
                new Uri("https://description.test"),
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256,
                new Uri("https://timestamp.test"),
                matcher,
                antiMatcher);

            AggregatingSignatureProviderTest test = new(
                $"{AppxContainerName}/a.dll",
                $"{AppxContainerName}/b.DLL",
                $"{AppxContainerName}/c.txt",
                $"{AppxContainerName}/d.exe",
                $"{AppxContainerName}/e.EXE",
                $"{AppxContainerName}/f/g.dll",
                $"{AppxContainerName}/f/h.txt",
                $"{AppxContainerName}/f/i.exe",
                $"{AppxContainerName}/DoNotSign/j.dll",
                $"{AppxContainerName}/DoNotSign/k.txt",
                $"{AppxContainerName}/DoNotSign/l/m.txt",
                $"{AppxContainerName}/DoNotSign/l/n.exe");

            await test.Provider.SignAsync(test.Files, options);

            ContainerSpy container = test.Containers[AppxContainerName];

            Assert.Equal(1, container.OpenAsync_CallCount);
            Assert.Equal(0, container.GetFiles_CallCount);
            Assert.Equal(2, container.GetFilesWithMatcher_CallCount);
            Assert.Equal(1, container.SaveAsync_CallCount);
            Assert.Equal(1, container.Dispose_CallCount);

            Assert.Collection(
                test.SignatureProviderSpy.SignedFiles,
                signedFile => Assert.Equal("a.dll", signedFile.Name),
                signedFile => Assert.Equal("b.DLL", signedFile.Name),
                signedFile => Assert.Equal("d.exe", signedFile.Name),
                signedFile => Assert.Equal("e.EXE", signedFile.Name),
                signedFile => Assert.Equal("g.dll", signedFile.Name),
                signedFile => Assert.Equal("i.exe", signedFile.Name),
                signedFile => Assert.Equal(AppxContainerName, signedFile.Name));
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

        [Fact]
        public async Task SignAsync_WhenFileIsZipContainerAndGlobAndAntiGlobPatternsAreUsed_SignsOnlyMatchingFiles()
        {
            const string fileListContents = 
@"**/*.dll
**/*.exe
!**/*.txt
!**/DoNotSign/**/*";
            ReadFileList(fileListContents, out Matcher matcher, out Matcher antiMatcher);

            SignOptions options = new(
                applicationName: "a",
                publisherName: "b",
                description: "c",
                new Uri("https://description.test"),
                HashAlgorithmName.SHA256,
                HashAlgorithmName.SHA256,
                new Uri("https://timestamp.test"),
                matcher,
                antiMatcher);

            AggregatingSignatureProviderTest test = new(
                $"{ZipContainerName}/a.dll",
                $"{ZipContainerName}/b.DLL",
                $"{ZipContainerName}/c.txt",
                $"{ZipContainerName}/d.exe",
                $"{ZipContainerName}/e.EXE",
                $"{ZipContainerName}/f/g.dll",
                $"{ZipContainerName}/f/h.txt",
                $"{ZipContainerName}/f/i.exe",
                $"{ZipContainerName}/DoNotSign/j.dll",
                $"{ZipContainerName}/DoNotSign/k.txt",
                $"{ZipContainerName}/DoNotSign/l/m.txt",
                $"{ZipContainerName}/DoNotSign/l/n.exe");

            await test.Provider.SignAsync(test.Files, options);

            ContainerSpy container = test.Containers[ZipContainerName];

            Assert.Equal(1, container.OpenAsync_CallCount);
            Assert.Equal(0, container.GetFiles_CallCount);
            Assert.Equal(2, container.GetFilesWithMatcher_CallCount);
            Assert.Equal(1, container.SaveAsync_CallCount);
            Assert.Equal(1, container.Dispose_CallCount);

            Assert.Collection(
                test.SignatureProviderSpy.SignedFiles,
                signedFile => Assert.Equal("a.dll", signedFile.Name),
                signedFile => Assert.Equal("b.DLL", signedFile.Name),
                signedFile => Assert.Equal("d.exe", signedFile.Name),
                signedFile => Assert.Equal("e.EXE", signedFile.Name),
                signedFile => Assert.Equal("g.dll", signedFile.Name),
                signedFile => Assert.Equal("i.exe", signedFile.Name));
        }

        private static void ReadFileList(string contents, out Matcher matcher, out Matcher antiMatcher)
        {
            MatcherFactory matcherFactory = new();
            FileListReader fileListReader = new(matcherFactory);

            using (MemoryStream stream = new(Encoding.UTF8.GetBytes(contents)))
            using (StreamReader reader = new(stream))
            {
                fileListReader.Read(reader, out matcher, out antiMatcher);
            }
        }
    }
}