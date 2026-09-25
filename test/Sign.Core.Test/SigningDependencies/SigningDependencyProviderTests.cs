// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class SigningDependencyProviderTests
    {
        [Fact]
        public void Constructor_WhenReadersIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new SigningDependencyProvider(readers: null!));

            Assert.Equal("readers", exception.ParamName);
        }

        [Fact]
        public void GetSigningDependencies_WhenFileIsNull_Throws()
        {
            SigningDependencyProvider provider = new(Enumerable.Empty<ISigningDependencyReader>());

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => provider.GetSigningDependencies(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Fact]
        public void GetSigningDependencies_WhenNoReaderMatches_ReturnsEmpty()
        {
            SigningDependencyProvider provider = new([new SigningDependencyReaderStub("a.owner", "a.owned")]);

            Assert.Empty(provider.GetSigningDependencies(new FileInfo("b.other")));
        }

        [Fact]
        public void GetSigningDependencies_WhenReaderMatches_ReturnsDependencies()
        {
            SigningDependencyProvider provider = new([new SigningDependencyReaderStub("a.owner", "a.owned")]);

            FileInfo dependency = Assert.Single(provider.GetSigningDependencies(new FileInfo("a.owner")));

            Assert.Equal(new FileInfo("a.owned").FullName, dependency.FullName);
        }

        [Fact]
        public void ExcludeOwnedFiles_WhenFilesIsNull_Throws()
        {
            SigningDependencyProvider provider = new(Enumerable.Empty<ISigningDependencyReader>());

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => provider.ExcludeOwnedFiles(files: null!));

            Assert.Equal("files", exception.ParamName);
        }

        [Fact]
        public void ExcludeOwnedFiles_WhenNothingIsOwned_ReturnsAllFiles()
        {
            SigningDependencyProvider provider = new(Enumerable.Empty<ISigningDependencyReader>());
            FileInfo[] files = [new("a.owner"), new("a.owned")];

            Assert.Equal(files, provider.ExcludeOwnedFiles(files));
        }

        [Fact]
        public void ExcludeOwnedFiles_WhenFileIsOwned_RemovesIt()
        {
            SigningDependencyProvider provider = new([new SigningDependencyReaderStub("a.owner", "a.owned")]);
            FileInfo[] files = [new("a.owner"), new("a.owned"), new("b.dll")];

            IReadOnlyList<FileInfo> actualFiles = provider.ExcludeOwnedFiles(files);

            Assert.Equal(["a.owner", "b.dll"], actualFiles.Select(file => file.Name));
        }

        [Fact]
        public void ExcludeOwnedFiles_WhenOwnerIsItselfOwned_RemovesBoth()
        {
            SigningDependencyProvider provider = new(
                [new SigningDependencyReaderStub("a.owner", "b.owner"), new SigningDependencyReaderStub("b.owner", "c.owned")]);
            FileInfo[] files = [new("a.owner"), new("b.owner"), new("c.owned")];

            IReadOnlyList<FileInfo> actualFiles = provider.ExcludeOwnedFiles(files);

            Assert.Equal(["a.owner"], actualFiles.Select(file => file.Name));
        }

        [Fact]
        public void ExcludeOwnedFiles_WhenOwnedFileIsNotPresent_ReturnsAllFiles()
        {
            SigningDependencyProvider provider = new([new SigningDependencyReaderStub("a.owner", "a.owned")]);
            FileInfo[] files = [new("a.owner")];

            Assert.Equal(files, provider.ExcludeOwnedFiles(files));
        }

        private sealed class SigningDependencyReaderStub : ISigningDependencyReader
        {
            private readonly string _ownerName;
            private readonly string[] _dependencyNames;

            internal SigningDependencyReaderStub(string ownerName, params string[] dependencyNames)
            {
                _ownerName = ownerName;
                _dependencyNames = dependencyNames;
            }

            public bool CanRead(FileInfo file)
            {
                return string.Equals(file.Name, _ownerName, StringComparison.OrdinalIgnoreCase);
            }

            public IReadOnlyList<FileInfo> GetSigningDependencies(FileInfo file)
            {
                return _dependencyNames.Select(name => new FileInfo(name)).ToList();
            }
        }
    }
}
