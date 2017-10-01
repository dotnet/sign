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

        [JsonProperty(PropertyName = "creationType")]
        public string CreationType { get; set; }

        [JsonProperty(PropertyName = "signInNames")]
        public SigninName[] SignInNames { get; set; }

        [JsonProperty(PropertyName = "otherMails")]
        public string[] OtherMails { get; set; }

        [JsonProperty(PropertyName = "givenName")]
        public string FirstName { get; set; }

        [JsonProperty(PropertyName = "surname")]
        public string LastName { get; set; }

        [JsonProperty(PropertyName = "displayName")]
        public string DisplayName { get; set; }

        [JsonProperty(PropertyName = "country")]
        public string Country { get; set; }

        //[JsonProperty(PropertyName = "mobile")]
        //public string Mobile { get; set; }
        [JsonProperty(PropertyName = "telephoneNumber")]
        public string TelephoneNumber { get; set; }

        [JsonProperty(PropertyName = "passwordProfile")]
        public PasswordProfile PasswordProfile { get; set; }

        [JsonProperty(PropertyName = "passwordPolicies")]
        public string PasswordPolicies { get; set; }

        public string KeyVaultUrl { get; set; }
        public string TimestampUrl { get; set; }
        public string KeyVaultCertificateName { get; set; }

#pragma warning disable CA1034 // Nested types should not be visible
        public class SigninName
        {
            [JsonProperty(PropertyName = "type")]
            public string Type { get; set; } = "emailAddress";

            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }

        }
#pragma warning restore CA1034 // Nested types should not be visible
    }
}