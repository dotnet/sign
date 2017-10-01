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

        [JsonProperty(PropertyName = "userPrincipalName")]
        public string UserPrincipalName { get; set; }

        [JsonProperty(PropertyName = "accountEnabled")]
        public bool? AccountEnabled { get; set; }

        [JsonProperty(PropertyName = "givenName")]
        public string FirstName { get; set; }

        [JsonProperty(PropertyName = "surname")]
        public string LastName { get; set; }

        [JsonProperty(PropertyName = "displayName")]
        public string DisplayName { get; set; }

        [JsonProperty(PropertyName = "userType")]
        public string UserType { get; set; }

        [JsonProperty(PropertyName = "passwordProfile")]
        public PasswordProfile PasswordProfile { get; set; }

        [JsonProperty(PropertyName = "passwordPolicies")]
        public string PasswordPolicies { get; set; }

        public bool? SignServiceConfigured { get; set; }

        public string KeyVaultUrl { get; set; }
        public string TimestampUrl { get; set; }
        public string KeyVaultCertificateName { get; set; }
    }
}