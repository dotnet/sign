using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core
{
    internal interface IManifestSigner
    {
        void Sign(FileInfo file, X509Certificate2 certificate, RSA rsaPrivateKey, SignOptions options);
    }
}