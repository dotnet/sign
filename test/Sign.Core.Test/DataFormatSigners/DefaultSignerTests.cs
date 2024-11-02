// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Moq;

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
            Mock<IAzureSignToolDataFormatSigner> mock = new(MockBehavior.Strict);

            mock.Setup(x => x.CanSign(It.IsAny<FileInfo>()))
                .Returns(expectedValue);

            IServiceCollection services = new ServiceCollection();

            services.AddLogging();
            services.AddSingleton(Mock.Of<IToolConfigurationProvider>());
            services.AddSingleton(Mock.Of<ISignatureAlgorithmProvider>());
            services.AddSingleton(Mock.Of<ICertificateProvider>());
            services.AddSingleton<IDataFormatSigner>(mock.Object);

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            DefaultSigner signer = new(serviceProvider);

            Assert.Equal(expectedValue, signer.CanSign(new FileInfo("file.dll")));

            mock.VerifyAll();
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
            Mock<IAzureSignToolDataFormatSigner> mock = new(MockBehavior.Strict);

            mock.Setup(x => x.SignAsync(It.IsAny<IEnumerable<FileInfo>>(), It.IsAny<SignOptions>()))
                .Returns(Task.CompletedTask);

            IServiceCollection services = new ServiceCollection();

            services.AddLogging();
            services.AddSingleton(Mock.Of<IToolConfigurationProvider>());
            services.AddSingleton(Mock.Of<ISignatureAlgorithmProvider>());
            services.AddSingleton(Mock.Of<ICertificateProvider>());
            services.AddSingleton<IDataFormatSigner>(mock.Object);

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            DefaultSigner signer = new(serviceProvider);

            await signer.SignAsync(Enumerable.Empty<FileInfo>(), _options);

            mock.VerifyAll();
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
            services.AddSingleton(Mock.Of<IToolConfigurationProvider>());
            services.AddSingleton(Mock.Of<ISignatureAlgorithmProvider>());
            services.AddSingleton(Mock.Of<ICertificateProvider>());
            services.AddSingleton<IDataFormatSigner, AzureSignToolSigner>();

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            return new DefaultSigner(serviceProvider);
        }
    }
}