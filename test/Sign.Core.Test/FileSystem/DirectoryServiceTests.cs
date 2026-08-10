// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Sign.Core.Test
{
    public class DirectoryServiceTests
    {
        private readonly ILogger<IDirectoryService> _logger;

        public DirectoryServiceTests()
        {
            _logger = Substitute.For<ILogger<IDirectoryService>>();
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new DirectoryService(logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public void CreateTemporaryDirectory_Always_CreatesDirectory()
        {
            using (DirectoryService service = new(_logger))
            {
                DirectoryInfo directory = service.CreateTemporaryDirectory();

                Assert.NotNull(directory);

                directory.Refresh();

                Assert.True(directory.Exists);
            }
        }

        [Fact]
        public void Delete_WhenDirectoryIsNull_Throws()
        {
            using (DirectoryService service = new(_logger))
            {
                ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                    () => service.Delete(directory: null!));

                Assert.Equal("directory", exception.ParamName);
            }
        }

        [Fact]
        public void Delete_Always_DeletesDirectory()
        {
            using (DirectoryService service = new(_logger))
            {
                DirectoryInfo directory = service.CreateTemporaryDirectory();

                service.Delete(directory);

                directory.Refresh();

                Assert.False(directory.Exists);
            }
        }


        [Fact]
        public void Dispose_Always_DeletesDirectory()
        {
            DirectoryInfo directory;

            using (DirectoryService service = new(_logger))
            {
                directory = service.CreateTemporaryDirectory();

                directory.Refresh();

                Assert.True(directory.Exists);
            }

            directory.Refresh();

            Assert.False(directory.Exists);
        }
    }
}