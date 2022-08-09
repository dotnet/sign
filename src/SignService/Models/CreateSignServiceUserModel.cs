using System;
using System.ComponentModel.DataAnnotations;

namespace SignService.Models
{
    public class CreateSignServiceUserModel
    {

        [Required(AllowEmptyStrings = false, ErrorMessage = "Display Name is required")]
        public string DisplayName { get; set; }
        [Required(AllowEmptyStrings = false, ErrorMessage = "Username is required")]
        public string Username { get; set; }
        public bool Configured { get; set; }
        public Uri KeyVaultUrl { get; set; }
        public string CertificateName { get; set; }
        public string TimestampUrl { get; set; }
    }
}
