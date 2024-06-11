// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sign.TestInfrastructure;

namespace Sign.SignatureProviders.CertificateStore.Test
{
    public class CertificateStoreServiceTests
    {
        private const string CertificateFingerprint = "a";
        private static readonly HashAlgorithmName CertificateFingerprintAlgorithm = HashAlgorithmName.SHA256;
        private const string? CryptoServiceProvider = "b";
        private const string? PrivateKeyContainer = "c";
        private const string? CertificateFilePath = null;
        private const string? CertificateFilePassword = null;
        private const bool IsMachineKeyContainer = true;
        private readonly IServiceProvider serviceProvider;

        public CertificateStoreServiceTests()
        {
            ServiceCollection services = new();
            services.AddSingleton<ILogger<CertificateStoreService>>(new TestLogger<CertificateStoreService>());
            serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void Constructor_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new CertificateStoreService(serviceProvider: null!, CertificateFingerprint, CertificateFingerprintAlgorithm, CryptoServiceProvider, PrivateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateFingerprintIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new CertificateStoreService(serviceProvider, certificateFingerprint: null!, CertificateFingerprintAlgorithm, CryptoServiceProvider, PrivateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer));

            Assert.Equal("certificateFingerprint", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenCertificateFingerprintIsEmpty_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new CertificateStoreService(serviceProvider, certificateFingerprint: string.Empty, CertificateFingerprintAlgorithm, CryptoServiceProvider, PrivateKeyContainer, CertificateFilePath, CertificateFilePassword, IsMachineKeyContainer));

            Assert.Equal("certificateFingerprint", exception.ParamName);
        }
    }
}
