// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core.Test
{
    internal sealed class CertificateChainServiceStub : ISignatureAlgorithmProvider, ICertificateProvider, IDisposable
    {
        internal X509Certificate2 Root { get; }
        internal X509Certificate2 Intermediate { get; }
        internal X509Certificate2 Leaf { get; }

        internal CertificateChainServiceStub()
        {
            // Unique names and no AIA extensions keep this chain independent of stores and downloads.
            DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddDays(-1);
            DateTimeOffset notAfter = notBefore.AddDays(2);
            string name = Guid.NewGuid().ToString("N");

            using RSA rootKey = RSA.Create(2048);
            CertificateRequest rootRequest = new($"CN=Root-{name}", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            using X509Certificate2 root = rootRequest.CreateSelfSigned(notBefore, notAfter);

            using RSA intermediateKey = RSA.Create(2048);
            CertificateRequest intermediateRequest = new($"CN=Intermediate-{name}", intermediateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            intermediateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            using X509Certificate2 intermediate = intermediateRequest.Create(root, notBefore, notAfter, RandomNumberGenerator.GetBytes(16));
            using X509Certificate2 intermediateWithKey = intermediate.CopyWithPrivateKey(intermediateKey);

            using RSA leafKey = RSA.Create(2048);
            CertificateRequest leafRequest = new($"CN=Leaf-{name}", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            using X509Certificate2 leaf = leafRequest.Create(intermediateWithKey, notBefore, notAfter, RandomNumberGenerator.GetBytes(16));

            Root = new X509Certificate2(root);
            Intermediate = new X509Certificate2(intermediate);
            Leaf = leaf.CopyWithPrivateKey(leafKey);
        }

        public Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new X509Certificate2(Leaf));
        }

        public Task<X509Certificate2Collection> GetAdditionalCertificatesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new X509Certificate2Collection(new[] { new X509Certificate2(Intermediate), new X509Certificate2(Root) }));
        }

        public Task<RSA> GetRsaAsync(CancellationToken cancellationToken = default)
        {
            using RSA privateKey = Leaf.GetRSAPrivateKey()!;
            return Task.FromResult(RSA.Create(privateKey.ExportParameters(includePrivateParameters: true)));
        }

        public void Dispose()
        {
            Leaf.Dispose();
            Intermediate.Dispose();
            Root.Dispose();
        }
    }
}
