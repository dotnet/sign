using System;
using System.Diagnostics;
using Newtonsoft.Json;
using SignService.Utils;

namespace SignService.Models
{
    /// <summary>
    /// Used to update/remove the values of the sign service attributes
    /// </summary>
    [JsonConverter(typeof(GraphUserConverterWithNulls))]
    [DebuggerDisplay("{DisplayName}")]
    public class GraphUserUpdate : IGraphUserExtensions
    {
        [JsonProperty(PropertyName = "objectId")]
        public Guid ObjectId { get; internal set; }

        /// <summary>
        ///     The name displayed in the address book for the user. This is usually the combination of the user's first name,
        ///     middle initial and last name. This property is required when a user is created and it cannot be cleared during
        ///     updates.
        /// </summary>
        [JsonProperty(PropertyName = "displayName")]
        public string DisplayName { get; set; }

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