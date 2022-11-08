using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Certificates;
using RSAKeyVaultProvider;

namespace Sign.Core
{
    internal sealed class KeyVaultService : IKeyVaultService
    {
        private Uri? _keyVaultUrl;
        private Task<KeyVaultCertificateWithPolicy>? _task;
        private TokenCredential? _tokenCredential;

        public async Task<X509Certificate2> GetCertificateAsync()
        {
            ThrowIfUninitialized();

            KeyVaultCertificateWithPolicy certificateWithPolicy = await _task!;

            return new X509Certificate2(certificateWithPolicy.Cer);
        }

        public async Task<RSA> GetRsaAsync()
        {
            ThrowIfUninitialized();

            KeyVaultCertificateWithPolicy certificateWithPolicy = await _task!;
            X509Certificate2 certificate = new(certificateWithPolicy.Cer);
            Uri keyIdentifier = certificateWithPolicy.KeyId;

            return RSAFactory.Create(_tokenCredential, keyIdentifier, certificate);
        }

        public void Initialize(Uri keyVaultUrl, TokenCredential tokenCredential, string certificateName)
        {
            ArgumentNullException.ThrowIfNull(keyVaultUrl, nameof(keyVaultUrl));
            ArgumentNullException.ThrowIfNull(tokenCredential, nameof(tokenCredential));
            ArgumentException.ThrowIfNullOrEmpty(certificateName, nameof(certificateName));

            _keyVaultUrl = keyVaultUrl;
            _tokenCredential = tokenCredential;

            _task = GetKeyVaultCertificateAsync(keyVaultUrl, tokenCredential, certificateName);
        }

        private static async Task<KeyVaultCertificateWithPolicy> GetKeyVaultCertificateAsync(
            Uri keyVaultUrl,
            TokenCredential tokenCredential,
            string certificateName)
        {
            CertificateClient client = new(keyVaultUrl, tokenCredential);
            Response<KeyVaultCertificateWithPolicy>? response =
                await client.GetCertificateAsync(certificateName).ConfigureAwait(false);

            return response.Value;
        }

        private void ThrowIfUninitialized()
        {
            if (_task is null)
            {
                throw new InvalidOperationException($"{nameof(Initialize)}(...) must be called first.");
            }
        }
    }
}