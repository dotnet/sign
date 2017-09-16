using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Azure.KeyVault;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.AspNetCore.Authentication;
using System.Security.Cryptography;

namespace SignService.Utils
{
    public interface IKeyVaultService
    {
        Task<string> GetAccessTokenAsync();
        Task<X509Certificate2> GetCertificateAsync();
        Task<RSA> ToRSA();
    }
    public class KeyVaultService : IKeyVaultService
    {
        readonly KeyVaultClient client;
        X509Certificate2 certificate;
        KeyIdentifier keyIdentifier;
        string validatedToken;
        private readonly Settings settings;
    
        public KeyVaultService(IOptionsSnapshot<Settings> settings, IOptionsSnapshot<AzureAdOptions> aadOptions, ILogger<KeyVaultService> logger)
        {
            async Task<string> Authenticate(string authority, string resource, string scope)
            {
                var context = new AuthenticationContext(authority);
                var credential = new ClientCredential(aadOptions.Value.ClientId, aadOptions.Value.ClientSecret);

                var result = await context.AcquireTokenAsync(resource, credential).ConfigureAwait(false);
                if (result == null)
                {
                    throw new InvalidOperationException("Authentication to Azure failed.");
                }
                validatedToken = result.AccessToken;
                return result.AccessToken;
            }

            client = new KeyVaultClient(Authenticate, new HttpClient());
            this.settings = settings.Value;
        }
        public async Task<string> GetAccessTokenAsync()
        {
            if(validatedToken == null)
                await GetCertificateAsync().ConfigureAwait(false); // trigger a get to populate the access token

            return validatedToken;
        }

        public async Task<X509Certificate2> GetCertificateAsync()
        {
            if (certificate == null)
            {
                var cert = await client.GetCertificateAsync(settings.CertificateInfo.KeyVaultUrl, settings.CertificateInfo.KeyVaultCertificateName).ConfigureAwait(false);
                certificate = new X509Certificate2(cert.Cer);
                keyIdentifier = cert.KeyIdentifier;
            }
            return certificate;
        }

        public async Task<RSA> ToRSA()
        {
            await GetCertificateAsync().ConfigureAwait(false);
            return client.ToRSA(keyIdentifier, certificate);
        }
    }
}
