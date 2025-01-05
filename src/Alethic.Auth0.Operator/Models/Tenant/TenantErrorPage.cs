using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Tenant
{

    public class TenantErrorPage
    {

        [JsonPropertyName("html")]
        public string? Html { get; set; }

        [JsonPropertyName("show_log_link")]
        public bool? ShowLogLink { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

    }

}
