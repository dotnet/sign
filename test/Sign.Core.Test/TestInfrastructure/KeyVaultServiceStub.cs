// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core.Test
{
    internal sealed class KeyVaultServiceStub : ISignatureAlgorithmProvider, ICertificateProvider, IDisposable
    {
        private RSA? _rsa;
        private X509Certificate2? _certificate;

        internal KeyVaultServiceStub()
        {
            _rsa = RSA.Create(keySizeInBits: 4096);

            CertificateRequest request = new("CN=test", _rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            DateTimeOffset now = DateTimeOffset.Now;

            _certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddMinutes(10));
        }

        public void Dispose()
        {
            _rsa?.Dispose();
            _certificate?.Dispose();

            GC.SuppressFinalize(this);
        }

        public Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new X509Certificate2(_certificate!));
        }

        public Task<RSA> GetRsaAsync(CancellationToken cancellationToken = default)
        {
            RSAParameters parameters = _rsa!.ExportParameters(includePrivateParameters: true);
            RSA rsa = RSA.Create(parameters);

            return Task.FromResult(rsa);
        }
    }
}