// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class SignableFileTypeByExtensionTests
    {
        [Fact]
        public void Constructor_WhenFileExtensionsIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new SignableFileTypeByExtension(fileExtensions: null!));
            Assert.Equal("fileExtensions", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenFileExtensionsIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new SignableFileTypeByExtension());
            Assert.Equal("fileExtensions", exception.ParamName);
        }

        [Fact]
        public void IsMatch_WhenFileIsNull_Throws()
        {
            SignableFileTypeByExtension fileType = new(fileExtensions: ".exe");

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => fileType.IsMatch(file: null!));
            Assert.Equal("file", exception.ParamName);
        }

        [Fact]
        public void IsMatch_WhenFileDoesNotMatch_ReturnsFalse()
        {
            FileInfo file = new(Path.Combine(Path.GetTempPath(), "file.abc"));
            SignableFileTypeByExtension fileType = new(fileExtensions: ".exe");

            Assert.False(fileType.IsMatch(file));
        }

        [Theory]
        [InlineData(".exe")]
        [InlineData(".EXE")]
        public void IsMatch_WhenFileMatches_ReturnsTrue(string fileExtension)
        {
            FileInfo file = new(Path.Combine(Path.GetTempPath(), $"file{fileExtension}"));
            SignableFileTypeByExtension fileType = new(fileExtensions: ".exe");

            Assert.True(fileType.IsMatch(file));
        }
    }
}
