using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;

namespace Sign.Core
{
    internal interface IKeyVaultService
    {
        Task<X509Certificate2> GetCertificateAsync();
        Task<RSA> GetRsaAsync();
        void Initialize(Uri keyVaultUrl, TokenCredential tokenCredential, string certificateName);
    }
}