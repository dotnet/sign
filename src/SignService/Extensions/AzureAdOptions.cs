namespace Microsoft.AspNetCore.Authentication
{
    public class AzureAdOptions
    {
        public string Audience { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string AADInstance { get; set; }
        public string Domain { get; set; }
        public string TenantId { get; set; }
        public string ApplicationObjectId { get; set; }

        public string CallbackPath { get; set; }
    }
}
