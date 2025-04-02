// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    public sealed class DynamicsBusinessCentralAppFileTypeTests : IDisposable
    {
        private readonly DirectoryService _directoryService;
        private readonly DynamicsBusinessCentralAppFileType _fileType;
        private readonly TemporaryDirectory _temporaryDirectory;

        public DynamicsBusinessCentralAppFileTypeTests()
        {
            _directoryService = new DirectoryService(Mock.Of<ILogger<IDirectoryService>>());
            _fileType = new DynamicsBusinessCentralAppFileType();
            _temporaryDirectory = new TemporaryDirectory(_directoryService);
        }

        public void Dispose()
        {
            _temporaryDirectory.Dispose();
            _directoryService.Dispose();
        }

        [Fact]
        public void IsMatch_WhenExtensionDoesNotMatch_ReturnsFalse()
        {
            FileInfo file = new(Path.Combine(_temporaryDirectory.Directory.FullName, "file.abc"));

            Assert.False(_fileType.IsMatch(file));
        }

        [Fact]
        public void IsMatch_WhenContentIsEmpty_ReturnsFalse()
        {
            FileInfo file = new(Path.Combine(_temporaryDirectory.Directory.FullName, "file.app"));

            File.WriteAllBytes(file.FullName, Array.Empty<byte>());

            Assert.False(_fileType.IsMatch(file));
        }

        [Fact]
        public void IsMatch_WhenContentDoesNotMatch_ReturnsFalse()
        {
            FileInfo file = new(Path.Combine(_temporaryDirectory.Directory.FullName, "file.app"));

            File.WriteAllText(file.FullName, "orange");

            Assert.False(_fileType.IsMatch(file));
        }

        [Theory]
        [InlineData(".app")]
        [InlineData(".APP")]
        public void IsMatch_WhenExtensionAndContentMatch_ReturnsTrue(string fileExtension)
        {
            FileInfo file = new(Path.Combine(_temporaryDirectory.Directory.FullName, $"file{fileExtension}"));

            File.WriteAllBytes(file.FullName, new byte[] { 0x4e, 0x41, 0x56, 0x58 });

            Assert.True(_fileType.IsMatch(file));
        }
    }
}
