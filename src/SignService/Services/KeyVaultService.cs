using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Azure.KeyVault;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using SignService.Utils;

namespace SignService.Services
{
    public interface IKeyVaultService
    {
        Task InitializeAccessTokenAsync(string incomingToken);
        void InitializeCertificateInfo(string timestampUrl, string keyVaultUrl, string certificateName);
        Task<X509Certificate2> GetCertificateAsync();
        Task<RSA> ToRSA();
        CertificateInfo CertificateInfo { get; }
        string AccessToken { get; }
    }
    public class KeyVaultService : IKeyVaultService
    {
        readonly KeyVaultClient client;
        X509Certificate2 certificate;
        KeyIdentifier keyIdentifier;
        readonly IOptionsSnapshot<ResourceIds> settings;
        readonly IOptionsSnapshot<AzureAdOptions> aadOptions;
    

        public KeyVaultService(IOptionsSnapshot<ResourceIds> settings, IOptionsSnapshot<AzureAdOptions> aadOptions, ILogger<KeyVaultService> logger)
        {
            Task<string> Authenticate(string authority, string resource, string scope)
            {
                return Task.FromResult(AccessToken);
            }

            client = new KeyVaultClient(new AutoRestCredential<KeyVaultClient>(Authenticate));

            this.settings = settings;
            this.aadOptions = aadOptions;
        }

        public CertificateInfo CertificateInfo { get; private set; }
    
        public string AccessToken { get; private set; }

        public async Task InitializeAccessTokenAsync(string incomingToken)
        {
            if (AccessToken == null)
            {
                var context = new AuthenticationContext($"{aadOptions.Value.AADInstance}{aadOptions.Value.TenantId}", null); // No token caching
                var credential = new ClientCredential(aadOptions.Value.ClientId, aadOptions.Value.ClientSecret);


                AuthenticationResult result = null;

                result = await context.AcquireTokenAsync(settings.Value.VaultId, credential, new UserAssertion(incomingToken));

                if (result == null)
                {
                    throw new InvalidOperationException("Authentication to Azure failed.");
                }

                AccessToken = result.AccessToken;
            }
        }

        public async Task<X509Certificate2> GetCertificateAsync()
        {
            if (certificate == null)
            {
                var cert = await client.GetCertificateAsync(CertificateInfo.KeyVaultUrl, CertificateInfo.CertificateName).ConfigureAwait(false);
                certificate = new X509Certificate2(cert.Cer);
                keyIdentifier = cert.KeyIdentifier;
            }
            return certificate;
        }

        public async Task<RSA> ToRSA()
        {
            await GetCertificateAsync()
                .ConfigureAwait(false);
            return client.ToRSA(keyIdentifier, certificate);
        }

        public void InitializeCertificateInfo(string timestampUrl, string keyVaultUrl, string certificateName)
        {

            // Lazy to store these after the ctor.
            CertificateInfo = new CertificateInfo
            {
                TimestampUrl = timestampUrl,
                KeyVaultUrl = keyVaultUrl,
                CertificateName = certificateName
            };
        }
    }
}
