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

namespace SignService.Services
{
    public interface IKeyVaultService
    {
        Task<string> GetAccessTokenAsync();
        Task<X509Certificate2> GetCertificateAsync();
        Task<RSA> ToRSA();
        CertificateInfo CertificateInfo { get; }
    }
    public class KeyVaultService : IKeyVaultService
    {
        readonly KeyVaultClient client;
        X509Certificate2 certificate;
        KeyIdentifier keyIdentifier;
        string validatedToken;
        readonly CertificateInfo certificateInfo;
        readonly IOptionsSnapshot<Settings> settings;
        readonly IOptionsSnapshot<AzureAdOptions> aadOptions;
        readonly IUser user;

        public KeyVaultService(IOptionsSnapshot<Settings> settings, IOptionsSnapshot<AzureAdOptions> aadOptions, IUser user, ILogger<KeyVaultService> logger)
        {
            async Task<string> Authenticate(string authority, string resource, string scope)
            {
                return await GetAccessTokenAsync().ConfigureAwait(false); 
            }

            client = new KeyVaultClient(Authenticate, new HttpClient());
            

            // This must be here because we add it in the request validation
            certificateInfo = new CertificateInfo
            {
                TimestampUrl = user.TimestampUrl,
                KeyVaultUrl = user.KeyVaultUrl,
                CertificateName = user.CertificateName
            };
            this.settings = settings;
            this.aadOptions = aadOptions;
            this.user = user;
        }

        public CertificateInfo CertificateInfo => certificateInfo;

        public async Task<string> GetAccessTokenAsync()
        {
            if (validatedToken == null)
            {
                var context = new AuthenticationContext($"{aadOptions.Value.AADInstance}{aadOptions.Value.TenantId}", null); // No token caching
                var credential = new ClientCredential(aadOptions.Value.ClientId, aadOptions.Value.ClientSecret);


                AuthenticationResult result = null;

                result = await context.AcquireTokenAsync(settings.Value.Resources.VaultId, credential, new UserAssertion(user.IncomingAccessToken));

                if (result == null)
                {
                    throw new InvalidOperationException("Authentication to Azure failed.");
                }
                validatedToken = result.AccessToken;
            }

            return validatedToken;
        }

        public async Task<X509Certificate2> GetCertificateAsync()
        {
            if (certificate == null)
            {
                var cert = await client.GetCertificateAsync(certificateInfo.KeyVaultUrl, certificateInfo.CertificateName).ConfigureAwait(false);
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
    }
}
