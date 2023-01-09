// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Sign.Core.Test
{
    public class DefaultSignatureProviderTests
    {
        private static readonly SignOptions _options = new(HashAlgorithmName.SHA256, new Uri("http://timestamp.test"));

        [Fact]
        public void Constructor_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new DefaultSignatureProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void SignatureProvider_WhenAzureSignToolSignatureProviderIsUnavailable_IsFallback()
        {
            DefaultSignatureProvider provider = CreateWithoutAzureSignTool();

            Assert.Null(provider.SignatureProvider as IAzureSignToolSignatureProvider);
            Assert.False(provider.CanSign(new FileInfo("file.dll")));
        }

        [Fact]
        public void SignatureProvider_WhenAzureSignToolSignatureProviderIsAvailable_IsFallback()
        {
            DefaultSignatureProvider provider = CreateWithAzureSignTool();

            Assert.IsAssignableFrom<IAzureSignToolSignatureProvider>(provider.SignatureProvider);
        }

        [Fact]
        public void CanSign_WhenAzureSignToolSignatureProviderIsUnavailable_ReturnsFalse()
        {
            DefaultSignatureProvider provider = CreateWithoutAzureSignTool();

            Assert.False(provider.CanSign(new FileInfo("file.dll")));
        }

        [Fact]
        public void CanSign_WhenAzureSignToolSignatureProviderIsAvailable_ReturnsTrue()
        {
            DefaultSignatureProvider provider = CreateWithAzureSignTool();

            Assert.True(provider.CanSign(new FileInfo("file.dll")));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CanSign_WhenIAzureSignToolSignatureProviderIsAvailable_ReturnsTrue(bool expectedValue)
        {
            Mock<IAzureSignToolSignatureProvider> mock = new(MockBehavior.Strict);

            mock.Setup(x => x.CanSign(It.IsAny<FileInfo>()))
                .Returns(expectedValue);

            IServiceCollection services = new ServiceCollection();

            services.AddLogging();
            services.AddSingleton(Mock.Of<IToolConfigurationProvider>());
            services.AddSingleton(Mock.Of<IKeyVaultService>());
            services.AddSingleton<ISignatureProvider>(mock.Object);

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            DefaultSignatureProvider provider = new(serviceProvider);

            Assert.Equal(expectedValue, provider.CanSign(new FileInfo("file.dll")));

            mock.VerifyAll();
        }

        [Fact]
        public async Task SignAsync_WhenFilesIsNull_Throws()
        {
            DefaultSignatureProvider provider = CreateWithAzureSignTool();

            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => provider.SignAsync(files: null!, _options));

            Assert.Equal("files", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenOptionsIsNull_Throws()
        {
            DefaultSignatureProvider provider = CreateWithAzureSignTool();

            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => provider.SignAsync(Enumerable.Empty<FileInfo>(), options: null!));

            Assert.Equal("options", exception.ParamName);
        }

        [Fact]
        public async Task SignAsync_WhenIAzureSignToolSignatureProviderIsAvailable_InvokesInnerProvider()
        {
            Mock<IAzureSignToolSignatureProvider> mock = new(MockBehavior.Strict);

            mock.Setup(x => x.SignAsync(It.IsAny<IEnumerable<FileInfo>>(), It.IsAny<SignOptions>()))
                .Returns(Task.CompletedTask);

            IServiceCollection services = new ServiceCollection();

            services.AddLogging();
            services.AddSingleton(Mock.Of<IToolConfigurationProvider>());
            services.AddSingleton(Mock.Of<IKeyVaultService>());
            services.AddSingleton<ISignatureProvider>(mock.Object);

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            DefaultSignatureProvider provider = new(serviceProvider);

            await provider.SignAsync(Enumerable.Empty<FileInfo>(), _options);

            mock.VerifyAll();
        }

        private static DefaultSignatureProvider CreateWithoutAzureSignTool()
        {
            IServiceCollection services = new ServiceCollection();
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            return new DefaultSignatureProvider(serviceProvider);
        }

        private static DefaultSignatureProvider CreateWithAzureSignTool()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddLogging();
            services.AddSingleton(Mock.Of<IToolConfigurationProvider>());
            services.AddSingleton(Mock.Of<IKeyVaultService>());
            services.AddSingleton<ISignatureProvider, AzureSignToolSignatureProvider>();

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            return new DefaultSignatureProvider(serviceProvider);
        }
    }
}