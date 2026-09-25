// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Text;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Sign.Core.Test
{
    public class MsiSigningDependencyReaderTests
    {
        private readonly MsiSigningDependencyReader _reader = new(Substitute.For<ILogger<ISigningDependencyReader>>());

        [Fact]
        public void CanRead_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _reader.CanRead(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Theory]
        [InlineData(".msi")]
        [InlineData(".msm")]
        [InlineData(".MSI")] // test case insensitivity
        public void CanRead_WhenFileExtensionMatches_ReturnsTrue(string extension)
        {
            Assert.True(_reader.CanRead(new FileInfo($"file{extension}")));
        }

        [Theory]
        [InlineData(".cab")]
        [InlineData(".dll")]
        public void CanRead_WhenFileExtensionDoesNotMatch_ReturnsFalse(string extension)
        {
            Assert.False(_reader.CanRead(new FileInfo($"file{extension}")));
        }

        [Fact]
        public void GetSigningDependencies_WhenFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _reader.GetSigningDependencies(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Fact]
        public void GetSigningDependencies_WhenFileDoesNotExist_ReturnsEmpty()
        {
            Assert.Empty(_reader.GetSigningDependencies(new FileInfo("nonexistent.msi")));
        }

        [Fact]
        public void GetSigningDependencies_WhenFileIsNotAnInstallPackage_ReturnsEmpty()
        {
            using (DirectoryServiceStub directoryService = new())
            {
                FileInfo file = new(Path.Combine(directoryService.CreateTemporaryDirectory().FullName, "a.msi"));

                File.WriteAllText(file.FullName, "not an install package");

                Assert.Empty(_reader.GetSigningDependencies(file));
            }
        }

        [Fact]
        public void GetSigningDependencies_WhenCabinetIsEmbedded_ReturnsEmpty()
        {
            using (DirectoryServiceStub directoryService = new())
            {
                DirectoryInfo directory = directoryService.CreateTemporaryDirectory();
                FileInfo msiFile = MsiCreator.Create(directory, [CreateFile(directory, "a.txt")], embedCabinet: true);

                Assert.Empty(_reader.GetSigningDependencies(msiFile));
            }
        }

        [Fact]
        public void GetSigningDependencies_WhenCabinetIsExternal_ReturnsCabinet()
        {
            using (DirectoryServiceStub directoryService = new())
            {
                DirectoryInfo directory = directoryService.CreateTemporaryDirectory();
                FileInfo msiFile = MsiCreator.Create(directory, [CreateFile(directory, "a.txt")], embedCabinet: false);

                FileInfo dependency = Assert.Single(_reader.GetSigningDependencies(msiFile));

                Assert.Equal(Path.Combine(directory.FullName, "test.cab"), dependency.FullName);
            }
        }

        private static FileInfo CreateFile(DirectoryInfo directory, string name)
        {
            FileInfo file = new(Path.Combine(directory.FullName, name));

            File.WriteAllBytes(file.FullName, Encoding.UTF8.GetBytes(name));

            return file;
        }
    }
}
