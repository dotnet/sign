using System;
using System.Diagnostics;
using Newtonsoft.Json;
using SignService.Utils;

namespace SignService.Models
{
    [JsonConverter(typeof(GraphUserConverter))]
    [DebuggerDisplay("{DisplayName}")]
    public class GraphUser : IGraphUserExtensions
    {

        [JsonProperty(PropertyName = "objectId")]
        public Guid? ObjectId { get; internal set; }

        /// <summary>
        ///     The user principal name (UPN) of the user. The UPN is an Internet-style login name for the user based on the
        ///     Internet standard RFC 822. By convention, this should map to the user's email name. The general format is
        ///     "aliasdomain", where domain must be present in the tenant's collection of verified domains. This property is
        ///     required when a user is created.
        /// </summary>
        [JsonProperty(PropertyName = "userPrincipalName")]
        public string UserPrincipalName { get; set; }

        /// <summary>
        ///     The given name (first name) of the user.
        /// </summary>
        [JsonProperty(PropertyName = "givenName")]
        public string GivenName { get; set; }

        /// <summary>
        ///     The user's surname (family name or last name).
        /// </summary>
        [JsonProperty(PropertyName = "surname")]
        public string Surname { get; set; }

        /// <summary>
        ///     The name displayed in the address book for the user. This is usually the combination of the user's first name,
        ///     middle initial and last name. This property is required when a user is created and it cannot be cleared during
        ///     updates.
        /// </summary>
        [JsonProperty(PropertyName = "displayName")]
        public string DisplayName { get; set; }

        /// <summary>
        ///     The mail alias for the user. This property must be specified when a user is created.
        /// </summary>
        [JsonProperty(PropertyName = "mailNickname")]
        public string MailNickname { get; set; }

        /// <summary>
        ///     true if the account is enabled; otherwise, false. This property is required when a user is created.
        /// </summary>
        [JsonProperty(PropertyName = "accountEnabled")]
        public bool? AccountEnabled { get; set; }

        /// <summary>
        ///     Specifies the UserType. Can be Member or Guest.
        /// </summary>
        [JsonProperty(PropertyName = "userType")]
        public string UserType { get; set; }

        /// <summary>
        ///     Specifies the password profile for the user. The profile contains the user's password. This property is required
        ///     when a user is created. The password in the profile must satisfy minimum requirements as specified by the
        ///     passwordPolicies property. By default, a strong password is required.
        /// </summary>
        [JsonProperty(PropertyName = "passwordProfile")]
        public PasswordProfile PasswordProfile { get; set; }

        /// <summary>
        ///     Specifies password policies for the user. This value is an enumeration with one possible value being
        ///     "DisableStrongPassword", which allows weaker passwords than the default policy to be specified.
        ///     "DisablePasswordExpiration" can also be specified. The two may be specified together; for example:
        ///     "DisablePasswordExpiration, DisableStrongPassword".
        /// </summary>
        [JsonProperty(PropertyName = "passwordPolicies")]
        public string PasswordPolicies { get; set; }

        /// <summary>
        ///     True to mark the user as configured for the sign service
        /// </summary>
        public bool? SignServiceConfigured { get; set; }

        /// <summary>
        ///     Url to the Key Vault: e.g., https://the-vault.vault.azure.net/
        /// </summary>
        public string KeyVaultUrl { get; set; }

        /// <summary>
        ///     Timestamping URL: e.g., http://timestamp.digicert.com
        /// </summary>
        public string TimestampUrl { get; set; }

        /// <summary>
        /// Certificate name within the vault
        /// </summary>
        public string KeyVaultCertificateName { get; set; }
    }
}