using System;

namespace SignService.Models
{
    public interface IGraphUserExtensions
    {
        Uri KeyVaultUrl { get; set; }
        string TimestampUrl { get; set; }
        string KeyVaultCertificateName { get; set; }
        bool? SignServiceConfigured { get; set; }
    }
}
