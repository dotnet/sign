using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

using Azure.Core;
using Azure.Security.KeyVault.Certificates;

using Microsoft.AspNetCore.Authentication.AzureAD.UI;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

using RSAKeyVaultProvider;

using SignService.Utils;

namespace SignService.Services
{
    public interface IKeyVaultService
    {
        Task InitializeAccessTokenAsync(string incomingToken);
        void InitializeCertificateInfo(string timestampUrl, Uri keyVaultUrl, string certificateName);
        Task<X509Certificate2> GetCertificateAsync();
        Task<RSA> ToRSA();
        CertificateInfo CertificateInfo { get; }
    }
    public class KeyVaultService : IKeyVaultService
    {
        CertificateClient client;
        TokenCredential tokenCredential;

        X509Certificate2 certificate;
        Uri keyIdentifier;
        readonly IOptionsSnapshot<ResourceIds> settings;
        readonly ILogger<KeyVaultService> logger;
        readonly AzureADOptions aadOptions;
    

        public KeyVaultService(IOptionsSnapshot<ResourceIds> settings, IOptionsSnapshot<AzureADOptions> aadOptions, ILogger<KeyVaultService> logger)
        {
           this.settings = settings;
            this.logger = logger;
            this.aadOptions = aadOptions.Get(AzureADDefaults.AuthenticationScheme);
        }

        public CertificateInfo CertificateInfo { get; private set; }
    
        public string AccessToken { get; private set; }

        public async Task InitializeAccessTokenAsync(string incomingToken)
        {
            
            var context = new AuthenticationContext($"{aadOptions.Instance}{aadOptions.TenantId}", null); // No token caching
            var credential = new ClientCredential(aadOptions.ClientId, aadOptions.ClientSecret);
            var result = await context.AcquireTokenAsync(settings.Value.VaultId, credential, new UserAssertion(incomingToken));
            if (result == null)
            {
                logger.LogError("Failed to authenticate to Key Vault on-behalf-of user");
                throw new InvalidOperationException("Authentication to Azure failed.");
            }

            tokenCredential = new AccessTokenCredential(result.AccessToken, result.ExpiresOn);            
        }

        public async Task<X509Certificate2> GetCertificateAsync()
        {
            if (certificate == null)
            {
                var cert = (await client.GetCertificateAsync(CertificateInfo.CertificateName).ConfigureAwait(false)).Value;
                certificate = new X509Certificate2(cert.Cer);
                keyIdentifier = cert.KeyId;
            }
            return certificate;
        }

        public async Task<RSA> ToRSA()
        {
            await GetCertificateAsync()
                .ConfigureAwait(false);

            return RSAFactory.Create(tokenCredential, keyIdentifier, certificate);
        }

        public void InitializeCertificateInfo(string timestampUrl, Uri keyVaultUrl, string certificateName)
        {

            // Lazy to store these after the ctor.
            CertificateInfo = new CertificateInfo
            {
                TimestampUrl = timestampUrl,
                KeyVaultUrl = keyVaultUrl,
                CertificateName = certificateName
            };

            client = new CertificateClient(keyVaultUrl, tokenCredential);
        }
    }
}
