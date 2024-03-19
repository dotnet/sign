// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    public sealed class PfxFilesFixture : IDisposable
    {
        private readonly TemporaryDirectory _directory;
        private readonly ConcurrentDictionary<Tuple<int, HashAlgorithmName>, FileInfo> _pfxFiles;

        public PfxFilesFixture()
        {
            _directory = new TemporaryDirectory(new DirectoryService(Mock.Of<ILogger<IDirectoryService>>()));
            _pfxFiles = new ConcurrentDictionary<Tuple<int, HashAlgorithmName>, FileInfo>();
        }

        internal X509Certificate2 GetPfx(int keySizeInBits, HashAlgorithmName hashAlgorithmName)
        {
            FileInfo file = _pfxFiles.GetOrAdd(
                new Tuple<int, HashAlgorithmName>(keySizeInBits, hashAlgorithmName),
                tuple => CreateSelfIssuedCertificate(tuple.Item1, tuple.Item2));

            return new X509Certificate2(file.FullName);
        }

        public void Dispose()
        {
            _directory.Dispose();
        }

        private FileInfo CreateSelfIssuedCertificate(int keySizeInBits, HashAlgorithmName hashAlgorithmName)
        {
            using (RSA rsa = RSA.Create(keySizeInBits))
            {
                CertificateRequest certificateRequest = new(
                    subjectName: "CN=Sign CLI, OU=TEST, O=TEST",
                    rsa,
                    hashAlgorithmName,
                    RSASignaturePadding.Pkcs1);

                certificateRequest.CertificateExtensions.Add(
                    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
                certificateRequest.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(new OidCollection() { Oids.CodeSigningEku }, critical: false));

                DateTimeOffset now = DateTimeOffset.Now;
                DateTimeOffset notBefore = now.AddMinutes(-1);
                DateTimeOffset notAfter = now.AddMinutes(15);

                using (X509Certificate2 certificate = certificateRequest.CreateSelfSigned(notBefore, notAfter))
                {
                    FileInfo file = new(Path.Combine(_directory.Directory.FullName, $"{certificate.Thumbprint}.pfx"));
                    byte[] bytes = certificate.Export(X509ContentType.Pfx);

                    File.WriteAllBytes(file.FullName, bytes);

                    return file;
                }
            }
        }
    }
}