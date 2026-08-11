// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Microsoft.Extensions.FileSystemGlobbing;
using NSubstitute;

namespace Sign.Core.Test
{
    public partial class AggregatingSignerTests
    {
        private static readonly SignOptions _options = new(HashAlgorithmName.SHA256, new Uri("http://timestamp.test"));

        [Fact]
        public void Constructor_WhenSignersIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSigner(
                    signers: null!,
                    Substitute.For<IDefaultDataFormatSigner>(),
                    Substitute.For<IContainerProvider>(),
                    Substitute.For<IFileMetadataService>(),
                    Substitute.For<IMatcherFactory>()));

            Assert.Equal("signers", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenDefaultSignerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSigner(
                    Enumerable.Empty<IDataFormatSigner>(),
                    defaultSigner: null!,
                    Substitute.For<IContainerProvider>(),
                    Substitute.For<IFileMetadataService>(),
                    Substitute.For<IMatcherFactory>()));

            Assert.Equal("defaultSigner", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenContainerProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSigner(
                    Enumerable.Empty<IDataFormatSigner>(),
                    Substitute.For<IDefaultDataFormatSigner>(),
                    containerProvider: null!,
                    Substitute.For<IFileMetadataService>(),
                    Substitute.For<IMatcherFactory>()));

            Assert.Equal("containerProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenFileMetadataServiceIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSigner(
                    Enumerable.Empty<IDataFormatSigner>(),
                    Substitute.For<IDefaultDataFormatSigner>(),
                    Substitute.For<IContainerProvider>(),
                    fileMetadataService: null!,
                    Substitute.For<IMatcherFactory>()));

            Assert.Equal("fileMetadataService", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenMatcherFactoryIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AggregatingSigner(
                    Enumerable.Empty<IDataFormatSigner>(),
                    Substitute.For<IDefaultDataFormatSigner>(),
                    Substitute.For<IContainerProvider>(),
                    Substitute.For<IFileMetadataService>(),
                    matcherFactory: null!));

            Assert.Equal("matcherFactory", exception.ParamName);
        }

        [Fact]
        public void CanSign_WhenFileIsNull_Throws()
        {
            AggregatingSigner aggregatingSigner = CreateSigner();

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => aggregatingSigner.CanSign(file: null!));

            Assert.Equal("file", exception.ParamName);
        }

        [Fact]
        public void CanSign_WhenSignerReturnsTrue_ReturnsTrue()
        {
            const string extension = ".xyz";
            FileInfo file = new($"file{extension}");
            IDataFormatSigner signer = Substitute.For<IDataFormatSigner>();

            signer.CanSign(Arg.Any<FileInfo>()).Returns(true);
            AggregatingSigner aggregatingSigner = CreateSigner(signer);

            Assert.True(aggregatingSigner.CanSign(file));
            signer.Received(1).CanSign(Arg.Is<FileInfo>(f => ReferenceEquals(f, file)));
            Assert.Single(signer.ReceivedCalls());
        }

        [Fact]
        public void CanSign_WhenSignerReturnsFalse_ReturnsFalse()
        {
            const string extension = ".xyz";
            FileInfo file = new($"file{extension}");
            IDataFormatSigner signer = Substitute.For<IDataFormatSigner>();

            signer.CanSign(Arg.Any<FileInfo>()).Returns(false);
            AggregatingSigner aggregatingSigner = CreateSigner(signer);

            Assert.False(aggregatingSigner.CanSign(file));
            signer.Received(1).CanSign(Arg.Is<FileInfo>(f => ReferenceEquals(f, file)));
            Assert.Single(signer.ReceivedCalls());
        }

        [Theory]
        [InlineData(".appxupload")]
        [InlineData(".msixupload")]
        [InlineData(".zip")]
        [InlineData(".ZIP")] // test case insensitivity
        public void CanSign_WhenExtensionIsSpecialCase_ReturnsTrue(string extension)
        {
            AggregatingSigner aggregatingSigner = CreateSigner();

            Assert.True(aggregatingSigner.CanSign(new FileInfo($"file{extension}")));
        }

        [Fact]
        public async Task SignAsync_WhenFilesIsNull_Throws()
        {
            AggregatingSigner aggregatingSigner = CreateSigner();

            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => aggregatingSigner.SignAsync(files: null!, _options));

            Assert.Equal("files", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenOptionsIsNull_Throws()
        {
            AggregatingSigner aggregatingSigner = CreateSigner();

            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => aggregatingSigner.SignAsync(Enumerable.Empty<FileInfo>(), options: null!));

            Assert.Equal("options", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenFilesIsEmpty_Returns()
        {
            AggregatingSigner aggregatingSigner = CreateSigner();

            await aggregatingSigner.SignAsync(Enumerable.Empty<FileInfo>(), _options);
        }

        private static AggregatingSigner CreateSigner(IDataFormatSigner? signer = null)
        {
            IEnumerable<IDataFormatSigner> signers;

            if (signer is null)
            {
                signers = Enumerable.Empty<IDataFormatSigner>();
            }
            else
            {
                signers = [signer];
            }

            IMatcherFactory matcherFactory = Substitute.For<IMatcherFactory>();
            matcherFactory.Create().Returns(new Matcher(StringComparison.OrdinalIgnoreCase));

            return new AggregatingSigner(
                signers,
                Substitute.For<IDefaultDataFormatSigner>(),
                Substitute.For<IContainerProvider>(),
                Substitute.For<IFileMetadataService>(),
                matcherFactory);
        }
    }
}