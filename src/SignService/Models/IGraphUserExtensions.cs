namespace SignService.Models
{
    public interface IGraphUserExtensions
    {
        string KeyVaultUrl { get; set; }
        string TimestampUrl { get; set; }
        string KeyVaultCertificateName { get; set; }
        bool? SignServiceConfigured { get; set; }
    }
}