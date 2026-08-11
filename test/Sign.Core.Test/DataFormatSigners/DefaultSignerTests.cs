// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Sign.Core.Test
{
    public class DefaultSignerTests
    {
        private static readonly SignOptions _options = new(HashAlgorithmName.SHA256, new Uri("http://timestamp.test"));

        [Fact]
        public void Constructor_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new DefaultSigner(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void Signer_WhenAzureSignToolSignerIsUnavailable_IsFallback()
        {
            DefaultSigner signer = CreateWithoutAzureSignTool();

            Assert.Null(signer.Signer as IAzureSignToolDataFormatSigner);
            Assert.False(signer.CanSign(new FileInfo("file.dll")));
        }

        [Fact]
        public void Signer_WhenAzureSignToolSignerIsAvailable_IsFallback()
        {
            DefaultSigner signer = CreateWithAzureSignTool();

            Assert.IsAssignableFrom<IAzureSignToolDataFormatSigner>(signer.Signer);
        }

        [Fact]
        public void CanSign_WhenAzureSignToolSignerIsUnavailable_ReturnsFalse()
        {
            DefaultSigner signer = CreateWithoutAzureSignTool();

            Assert.False(signer.CanSign(new FileInfo("file.dll")));
        }

        [Fact]
        public void CanSign_WhenAzureSignToolSignerIsAvailable_ReturnsTrue()
        {
            DefaultSigner signer = CreateWithAzureSignTool();

            Assert.True(signer.CanSign(new FileInfo("file.dll")));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CanSign_WhenIAzureSignToolSignerIsAvailable_ReturnsTrue(bool expectedValue)
        {
            FileInfo file = new("file.dll");
            IAzureSignToolDataFormatSigner mock = Substitute.For<IAzureSignToolDataFormatSigner>();
            mock.CanSign(Arg.Any<FileInfo>()).Returns(expectedValue);

            IServiceCollection services = new ServiceCollection();

            services.AddLogging();
            services.AddSingleton(Substitute.For<IToolConfigurationProvider>());
            services.AddSingleton(Substitute.For<ISignatureAlgorithmProvider>());
            services.AddSingleton(Substitute.For<ICertificateProvider>());
            services.AddSingleton<IDataFormatSigner>(mock);

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            DefaultSigner signer = new(serviceProvider);

            Assert.Equal(expectedValue, signer.CanSign(file));
            mock.Received(1).CanSign(Arg.Is<FileInfo>(f => ReferenceEquals(f, file)));
            Assert.Single(mock.ReceivedCalls());
        }

        [Fact]
        public async Task SignAsync_WhenFilesIsNull_Throws()
        {
            DefaultSigner signer = CreateWithAzureSignTool();

            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => signer.SignAsync(files: null!, _options));

            Assert.Equal("files", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenOptionsIsNull_Throws()
        {
            DefaultSigner signer = CreateWithAzureSignTool();

            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => signer.SignAsync(Enumerable.Empty<FileInfo>(), options: null!));

            Assert.Equal("options", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenIAzureSignToolSignerIsAvailable_InvokesInnerProvider()
        {
            IEnumerable<FileInfo> files = [];
            IAzureSignToolDataFormatSigner mock = Substitute.For<IAzureSignToolDataFormatSigner>();
            mock.SignAsync(Arg.Any<IEnumerable<FileInfo>>(), Arg.Any<SignOptions>()).Returns(Task.CompletedTask);

            IServiceCollection services = new ServiceCollection();

            services.AddLogging();
            services.AddSingleton(Substitute.For<IToolConfigurationProvider>());
            services.AddSingleton(Substitute.For<ISignatureAlgorithmProvider>());
            services.AddSingleton(Substitute.For<ICertificateProvider>());
            services.AddSingleton<IDataFormatSigner>(mock);

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            DefaultSigner signer = new(serviceProvider);

            await signer.SignAsync(files, _options);
            await mock.Received(1).SignAsync(
                Arg.Is<IEnumerable<FileInfo>>(value => ReferenceEquals(value, files)),
                Arg.Is<SignOptions>(value => ReferenceEquals(value, _options)));
            Assert.Single(mock.ReceivedCalls());
        }

        private static DefaultSigner CreateWithoutAzureSignTool()
        {
            IServiceCollection services = new ServiceCollection();
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            return new DefaultSigner(serviceProvider);
        }

        private static DefaultSigner CreateWithAzureSignTool()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddLogging();
            services.AddSingleton(Substitute.For<IToolConfigurationProvider>());
            services.AddSingleton(Substitute.For<ISignatureAlgorithmProvider>());
            services.AddSingleton(Substitute.For<ICertificateProvider>());
            services.AddSingleton<IDataFormatSigner, AzureSignToolSigner>();

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            return new DefaultSigner(serviceProvider);
        }
    }
}