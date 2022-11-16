// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class FileMetadataServiceTests
    {
        private readonly FileMetadataService _service = new();

        [Fact]
        public void IsPortableExecutable_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _service.IsPortableExecutable(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Fact]
        public void IsPortableExecutable_WhenFileIsNotMatch_ReturnsFalse()
        {
            using (TemporaryFile temporaryFile = CreateFakeNonPortableExecutableFile())
            {
                Assert.False(_service.IsPortableExecutable(temporaryFile.File));
            }
        }

        [Fact]
        public void IsPortableExecutable_WhenFileIsMatch_ReturnsTrue()
        {
            using (TemporaryFile temporaryFile = CreateFakePortableExecutableFile())
            {
                Assert.True(_service.IsPortableExecutable(temporaryFile.File));
            }
        }

        private TemporaryFile CreateFakeNonPortableExecutableFile()
        {
            TemporaryFile temporaryFile = new();

            using (FileStream stream = temporaryFile.File.OpenWrite())
            using (StreamWriter writer = new(stream))
            {
                writer.Write("Hello, World!");
            }

            return temporaryFile;
        }

        private TemporaryFile CreateFakePortableExecutableFile()
        {
            TemporaryFile temporaryFile = new();

            using (FileStream stream = temporaryFile.File.OpenWrite())
            {
                stream.Write("MZ"u8);
            }

            return temporaryFile;
        }
    }
}