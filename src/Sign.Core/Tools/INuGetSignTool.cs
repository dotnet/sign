using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Core
{
    internal interface INuGetSignTool : ITool
    {
        Task<bool> SignAsync(FileInfo file, RSA rsaPrivateKey, X509Certificate2 certificate, SignOptions options);
    }
}