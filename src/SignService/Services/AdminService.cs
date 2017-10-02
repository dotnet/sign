using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using SignService.Models;
using SignService.Utils;
using System.Security.Cryptography;
using System.Text;

namespace SignService.Services
{
    public interface IAdminService
    {
        Task<(GraphUser, string)> CreateUserAsync(string displayName, string username, string keyVaultUrl, string keyVaultCertName, string timestampUrl);
        Task UpdateUserAsync(Guid objectId, string displayName, bool? configured, string keyVaultUrl, string keyVaultCertName, string timestampUrl);
        Task<IEnumerable<GraphUser>> GetUsersAsync(string displayName);
        Task<GraphUser> GetUserByObjectIdAsync(Guid objectId);
        Task<string> UpdatePasswordAsync(Guid objectId);
        Task<IEnumerable<GraphUser>> GetConfiguredUsersAsync();
        Task RegisterExtensionPropertiesAsync();
        Task UnRegisterExtensionPropertiesAsync();
    }

    public class AdminService : IAdminService
    {
        readonly AdminConfig configuration;
        readonly AzureAdOptions azureAdOptions;
        readonly IGraphHttpService graphHttpService;
        readonly string extensionPrefix;
        
        public AdminService(IOptionsSnapshot<AdminConfig> configuration, IOptionsSnapshot<AzureAdOptions> azureAdOptions, IGraphHttpService graphHttpService)
        {
            this.configuration = configuration.Value;
            this.azureAdOptions = azureAdOptions.Value;
            this.graphHttpService = graphHttpService;
            extensionPrefix = $"extension_{azureAdOptions.Value.ClientId.Replace("-", "")}_";
        }

        public async Task RegisterExtensionPropertiesAsync()
        {
            var extensionProperties = new[]
            {
                new ExtensionProperty
                {
                    DataType = "Boolean",
                    Name = "signServiceConfigured",
                    TargetObjects = new[]
                    {
                        "User"
                    }
                },
                new ExtensionProperty
                {
                    DataType = "String",
                    Name = "keyVaultUrl",
                    TargetObjects = new[]
                    {
                        "User"
                    }
                },
                new ExtensionProperty
                {
                    DataType = "String",
                    Name = "timestampUrl",
                    TargetObjects = new[]
                    {
                        "User"
                    }
                },
                new ExtensionProperty
                {
                    DataType = "String",
                    Name = "keyVaultCertificateName",
                    TargetObjects = new[]
                    {
                        "User"
                    }
                }
            };

            var created = new List<ExtensionProperty>();

            foreach (var prop in extensionProperties)
            {
                var c = await graphHttpService.Post<ExtensionProperty, ExtensionProperty>($"/applications/{azureAdOptions.ApplicationObjectId}/extensionProperties?api-version=1.6", prop).ConfigureAwait(false);
                created.Add(c);
            }
        }

        public async Task UnRegisterExtensionPropertiesAsync()
        {
            var uri = $"/applications/{azureAdOptions.ApplicationObjectId}/extensionProperties?api-version=1.6";

            var result = await graphHttpService.Get<ExtensionProperty>(uri).ConfigureAwait(false);

            foreach (var prop in result)
            {
                await graphHttpService.Delete($"/applications/{azureAdOptions.ApplicationObjectId}/extensionProperties/{prop.ObjectId}?api-version=1.6").ConfigureAwait(false);
            }
        }

        public async Task<IEnumerable<GraphUser>> GetUsersAsync(string displayName)
        {
            displayName = displayName.Replace("'", ""); // don't unescape

            var uri =$"/users?api-version=1.6&$filter=startswith(displayName, '{displayName}') or startswith(givenName, '{displayName}') or startswith(surname, '{displayName}')";
            
            var result = await graphHttpService.Get<GraphUser>(uri).ConfigureAwait(false);
            
            return result;
        }

        public async Task<GraphUser> GetUserByObjectIdAsync(Guid objectId)
        {
            var uri = $"/users/{objectId}?api-version=1.6";

            var result = await graphHttpService.GetScalar<GraphUser>(uri).ConfigureAwait(false);

            return result;
        }


        public async Task<(GraphUser, string)> CreateUserAsync(string displayName, string username, string keyVaultUrl, string keyVaultCertName, string timestampUrl)
        {
            var uri = $"/users?api-version=1.6";

            var password = GetRandomPassword();

            var user = new GraphUser
            {
                DisplayName = displayName,
                UserPrincipalName = username,
                UserType = "Guest",
                KeyVaultUrl = keyVaultUrl,
                KeyVaultCertificateName = keyVaultCertName,
                TimestampUrl = timestampUrl,
                AccountEnabled = true,
                MailNickname = username.Substring(0, username.IndexOf('@')), // use the username up to the @
                PasswordProfile = new PasswordProfile
                {
                    ForceChangePasswordNextLogin = false,
                    Password = password
                },
                PasswordPolicies = "DisablePasswordExpiration, DisableStrongPassword"
            };


            var result = await graphHttpService.Post<GraphUser, GraphUser>(uri, user).ConfigureAwait(false);

            return (result, password);
        }

        public async Task<string> UpdatePasswordAsync(Guid objectId)
        {
            // Update the password on the account. For safety, we'll make sure this account is configured as a SignService Account first

            var user = await GetUserByObjectIdAsync(objectId).ConfigureAwait(false);
            var uri = $"/users/{user.ObjectId}?api-version=1.6";
            var password = GetRandomPassword();

            if (user.SignServiceConfigured == true)
            {
                // new user so we don't touch existing values
                var toUpdate = new GraphUser
                {
                    ObjectId = user.ObjectId,
                    PasswordProfile = new PasswordProfile
                    {
                        ForceChangePasswordNextLogin = false,
                        Password = password
                    }
                };

                await graphHttpService.Patch(uri, toUpdate).ConfigureAwait(false);
                return password;
            }
            else
            {
                throw new InvalidOperationException("Can only update password of SignServiceConfigured users");
            }
        }

        public async Task UpdateUserAsync(Guid objectId, string displayName, bool? configured, string keyVaultUrl, string keyVaultCertName, string timestampUrl)
        {
            var uri = $"/users/{objectId}?api-version=1.6";

            // We never want to set DisplayName to blank
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Argument cannot be blank when configured is true", nameof(displayName));

            if (configured == true)
            {
                // validate the args are present
                if (string.IsNullOrWhiteSpace(keyVaultUrl)) throw new ArgumentException("Argument cannot be blank when configured is true", nameof(keyVaultUrl));
                if (string.IsNullOrWhiteSpace(keyVaultCertName)) throw new ArgumentException("Argument cannot be blank when configured is true", nameof(keyVaultCertName));
                if (string.IsNullOrWhiteSpace(timestampUrl)) throw new ArgumentException("Argument cannot be blank when configured is true", nameof(timestampUrl));
            }

            var user = new GraphUserUpdate
            {
                ObjectId = objectId,
                DisplayName = displayName,
                SignServiceConfigured = configured,
                KeyVaultUrl = string.IsNullOrWhiteSpace(keyVaultUrl) ? null : keyVaultUrl,
                KeyVaultCertificateName = string.IsNullOrWhiteSpace(keyVaultCertName) ? null : keyVaultCertName,
                TimestampUrl = string.IsNullOrWhiteSpace(timestampUrl) ? null : timestampUrl
            }; 

            await graphHttpService.Patch(uri, user).ConfigureAwait(false);
        }

        public async Task<IEnumerable<GraphUser>> GetConfiguredUsersAsync()
        {
            // This may throw if we run this query before the extension is registered. Just return empty
            try
            {
                var uri = $"/users?api-version=1.6&$filter={extensionPrefix}signServiceConfigured eq true or {extensionPrefix}signServiceConfigured eq false";

                var result = await graphHttpService.Get<GraphUser>(uri).ConfigureAwait(false);

                return result;
            }
            catch (Exception)
            {
                return Enumerable.Empty<GraphUser>();
            }
        }

        private static string GetRandomPassword()
        {
            // From @vcsjones, thanks!
            const string ALLOWED_CHARS = @"0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ@#$%^&*-_!+=[]{}|\:',.?/`~""();";
            const int PASSWORD_LENGTH = 16; // AAD has a max of 16 char passwords
            var builder = new StringBuilder();
            using (var rng = RandomNumberGenerator.Create())
            {
                var data = new byte[PASSWORD_LENGTH];
                rng.GetBytes(data);
                for (var i = 0; i < data.Length; i++)
                {
                    builder.Append(ALLOWED_CHARS[data[i] % ALLOWED_CHARS.Length]);
                }

                return builder.ToString();
            }
        }

    }
}
