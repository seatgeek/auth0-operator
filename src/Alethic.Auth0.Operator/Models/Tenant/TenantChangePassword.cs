using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Tenant
{

    public class TenantChangePassword
    {

        [JsonPropertyName("enabled")]
        public bool? Enabled { get; set; }

        [JsonPropertyName("html")]
        public string? Html { get; set; }

    }

}
