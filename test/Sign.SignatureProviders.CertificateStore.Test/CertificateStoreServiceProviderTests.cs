// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sign.Core;
using Sign.TestInfrastructure;

namespace Sign.SignatureProviders.CertificateStore.Test
{
    public class CertificateStoreServiceProviderTests
    {
        private const string CertificateFingerprint = "a";
        private static readonly HashAlgorithmName CertificateFingerprintAlgorithm = HashAlgorithmName.SHA256;
        private const string? CryptoServiceProvider = "b";
        private const string? PrivateKeyContainer = "c";
        private const string? CertificateFilePath = null;
        private const string? CertificateFilePassword = null;
        private const bool IsMachineKeyContainer = true;
        private readonly IServiceProvider serviceProvider;

        public CertificateStoreServiceProviderTests()
        {
            ServiceCollection services = new();
            services.AddSingleton<ILogger<CertificateStoreService>>(new TestLogger<CertificateStoreService>());
            serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void Constructor_WhenCertificateFingerprintIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new CertificateStoreServiceProvider(certificateFingerprint: null!, CertificateFingerprintAlgorithm, CryptoServiceProvider, PrivateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer));

            Assert.Equal("certificateFingerprint", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateFingerprintIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new CertificateStoreServiceProvider(certificateFingerprint: string.Empty, CertificateFingerprintAlgorithm, CryptoServiceProvider, PrivateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer));

            Assert.Equal("certificateFingerprint", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCryptoServiceProviderIsNullAndPrivateKeyContainerIsNot_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new CertificateStoreServiceProvider(CertificateFingerprint, CertificateFingerprintAlgorithm, cryptoServiceProvider: null, PrivateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer));

            Assert.Equal("cryptoServiceProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCryptoServiceProviderIsEmptyAndPrivateKeyContainerIsNot_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new CertificateStoreServiceProvider(CertificateFingerprint, CertificateFingerprintAlgorithm, cryptoServiceProvider: string.Empty, PrivateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer));

            Assert.Equal("cryptoServiceProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenPrivateKeyContainerIsNullAndCryptoServiceProviderIsNot_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new CertificateStoreServiceProvider(CertificateFingerprint, CertificateFingerprintAlgorithm, CryptoServiceProvider, privateKeyContainer: null, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer));

            Assert.Equal("privateKeyContainer", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenPrivateKeyContainerIsEmptyAndCryptoServiceProviderIsNot_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new CertificateStoreServiceProvider(CertificateFingerprint, CertificateFingerprintAlgorithm, CryptoServiceProvider, privateKeyContainer: string.Empty, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer));

            Assert.Equal("privateKeyContainer", exception.ParamName);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData(null, "")]
        [InlineData("", null)]
        [InlineData("", "")]
        public void Constructor_WhenPrivateKeyContainerAndCryptoServiceProviderAreBothNullOrEmpty_DoesNotThrow(string? cryptoServiceProvider, string? privateKeyContainer)
        {
            CertificateStoreServiceProvider provider = new(CertificateFingerprint, CertificateFingerprintAlgorithm, cryptoServiceProvider, privateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer);
        }

        [Fact]
        public void GetSignatureAlgorithmProvider_WhenServiceProviderIsNull_Throws()
        {
            CertificateStoreServiceProvider provider = new(CertificateFingerprint, CertificateFingerprintAlgorithm, CryptoServiceProvider, PrivateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer);

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => provider.GetSignatureAlgorithmProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void GetSignatureAlgorithmProvider_ReturnsSameInstance()
        {
            CertificateStoreServiceProvider provider = new(CertificateFingerprint, CertificateFingerprintAlgorithm, CryptoServiceProvider, PrivateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer);

            ConcurrentBag<ISignatureAlgorithmProvider> signatureAlgorithmProviders = [];
            Parallel.For(0, 2, (_, _) =>
            {
                signatureAlgorithmProviders.Add(provider.GetSignatureAlgorithmProvider(serviceProvider));
            });

            Assert.Equal(2, signatureAlgorithmProviders.Count);
            Assert.Same(signatureAlgorithmProviders.First(), signatureAlgorithmProviders.Last());
        }

        [Fact]
        public void GetCertificateProvider_WhenServiceProviderIsNull_Throws()
        {
            CertificateStoreServiceProvider provider = new(CertificateFingerprint, CertificateFingerprintAlgorithm, CryptoServiceProvider, PrivateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer);

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => provider.GetCertificateProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void GetCertificateProvider_ReturnsSameInstance()
        {
            CertificateStoreServiceProvider provider = new(CertificateFingerprint, CertificateFingerprintAlgorithm, CryptoServiceProvider, PrivateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer);

            ConcurrentBag<ICertificateProvider> certificateProviders = [];
            Parallel.For(0, 2, (_, _) =>
            {
                certificateProviders.Add(provider.GetCertificateProvider(serviceProvider));
            });

            Assert.Equal(2, certificateProviders.Count);
            Assert.Same(certificateProviders.First(), certificateProviders.Last());
        }
    }
}
