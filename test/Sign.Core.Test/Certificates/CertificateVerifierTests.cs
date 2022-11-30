// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    public class CertificateVerifierTests
    {
        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new CertificateVerifier(logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public void Verify_WhenCertificateIsNull_Throws()
        {
            CertificateVerifier verifier = new(Mock.Of<ILogger<ICertificateVerifier>>());

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => verifier.Verify(certificate: null!));

            Assert.Equal("certificate", exception.ParamName);
        }

        [Fact]
        public void Verify_WhenCertificateIsNotYetTimeValid_LogsWarning()
        {
            Logger logger = new(Resources.CertificateIsNotYetTimeValid);
            CertificateVerifier verifier = new(logger);
            DateTimeOffset now = DateTimeOffset.Now;

            using (X509Certificate2 certificate = CreateCertificate(
                notBefore: now.AddDays(1),
                notAfter: now.AddDays(2)))
            {
                verifier.Verify(certificate);
            }

            Assert.Equal(1, logger.Log_CallCount);
        }

        [Fact]
        public void Verify_WhenCertificateIsExpired_LogsWarning()
        {
            Logger logger = new(Resources.CertificateIsExpired);
            CertificateVerifier verifier = new(logger);
            DateTimeOffset now = DateTimeOffset.Now;

            using (X509Certificate2 certificate = CreateCertificate(
                notBefore: now.AddDays(-2),
                notAfter: now.AddDays(-1)))
            {
                verifier.Verify(certificate);
            }

            Assert.Equal(1, logger.Log_CallCount);
        }

        [Fact]
        public void Verify_WhenCertificateIsTimeValid_DoesNotLogWarning()
        {
            Logger logger = new();
            CertificateVerifier verifier = new(logger);
            DateTimeOffset now = DateTimeOffset.Now;

            using (X509Certificate2 certificate = CreateCertificate(
                notBefore: now.AddDays(-1),
                notAfter: now.AddDays(1)))
            {
                verifier.Verify(certificate);
            }

            Assert.Equal(0, logger.Log_CallCount);
        }

        private static X509Certificate2 CreateCertificate(DateTimeOffset notBefore, DateTimeOffset notAfter)
        {
            using (RSA keyPair = RSA.Create(keySizeInBits: 3072))
            {
                CertificateRequest certificateRequest = new(
                    "CN=test.test",
                    keyPair,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                return certificateRequest.CreateSelfSigned(notBefore, notAfter);
            }
        }

        private sealed class Logger : ILogger<ICertificateVerifier>
        {
            private readonly string? _expectedMessage;

            internal int Log_CallCount { get; private set; }

            internal Logger(string? expectedMessage = null)
            {
                _expectedMessage = expectedMessage;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return new NoOpDisposable();
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                ++Log_CallCount;

                Assert.Equal(LogLevel.Warning, logLevel);

                string actualMessage = formatter(state, exception);

                Assert.Equal(_expectedMessage, actualMessage);
            }

            private sealed class NoOpDisposable : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }
    }
}