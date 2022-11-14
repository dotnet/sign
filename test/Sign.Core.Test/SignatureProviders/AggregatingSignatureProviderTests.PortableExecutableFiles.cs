namespace Sign.Core.Test
{
    public partial class AggregatingSignatureProviderTests
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

            AggregatingSignatureProviderTest test = new(files);

            await test.Provider.SignAsync(test.Files, _options);

            Assert.Empty(test.Containers);

            Assert.Collection(
                test.SignatureProviderSpy.SignedFiles,
                signedFile => Assert.Equal("a.dll", signedFile.Name),
                signedFile => Assert.Equal("a.dll", signedFile.Name),
                signedFile => Assert.Equal("b.dll", signedFile.Name),
                signedFile => Assert.Equal("c.dll", signedFile.Name));
        }
    }
}