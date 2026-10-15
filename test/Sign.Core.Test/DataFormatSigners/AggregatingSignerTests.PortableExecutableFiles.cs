// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using NSubstitute;

namespace Sign.Core.Test
{
    public partial class AggregatingSignerTests
    {
        [Fact]
        public async Task SignAsync_WhenFilesAreLoosePortableExecutableFiles_SignsAllFiles()
        {
            string[] files = new[]
            {
                "a.dll",
                "directory0/a.dll",
                "directory0/directory1/b.dll",
                "directory2/c.dll"
            };

            AggregatingSignerTest test = new(files);

            await test.Signer.SignAsync(test.Files, _options);

            Assert.Empty(test.Containers);

            Assert.Collection(
                test.SignerSpy.SignedFiles,
                signedFile => Assert.Equal("a.dll", signedFile.Name),
                signedFile => Assert.Equal("a.dll", signedFile.Name),
                signedFile => Assert.Equal("b.dll", signedFile.Name),
                signedFile => Assert.Equal("c.dll", signedFile.Name));
        }

        [Fact]
        public async Task SignAsync_WhenUnassignedPortableExecutableFileIsRepeated_SignsFileOnce()
        {
            string path = Path.Combine(Path.GetTempPath(), "a.bin");
            FileInfo file = new(path);
            FileInfo sameFile = new(path);
            IDataFormatSigner signer = Substitute.For<IDataFormatSigner>();
            IDataFormatSigner defaultSigner = Substitute.For<IDataFormatSigner>();
            IDefaultDataFormatSigner defaultDataFormatSigner =
                Substitute.For<IDefaultDataFormatSigner>();
            IFileMetadataService fileMetadataService =
                Substitute.For<IFileMetadataService>();
            List<FileInfo>? signedFiles = null;

            defaultDataFormatSigner.Signer.Returns(defaultSigner);
            fileMetadataService
                .IsPortableExecutable(Arg.Any<FileInfo>())
                .Returns(true);
            defaultSigner
                .SignAsync(
                    Arg.Do<IEnumerable<FileInfo>>(
                        files => signedFiles = files.ToList()),
                    Arg.Any<SignOptions>())
                .Returns(Task.CompletedTask);

            AggregatingSigner aggregatingSigner = new(
                [signer],
                defaultDataFormatSigner,
                Substitute.For<IContainerProvider>(),
                fileMetadataService,
                Substitute.For<IMatcherFactory>());

            await aggregatingSigner.SignAsync(
                new[] { file, sameFile },
                _options);

            Assert.NotNull(signedFiles);
            FileInfo signedFile = Assert.Single(signedFiles);
            Assert.Same(file, signedFile);
        }
    }
}