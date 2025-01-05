using System.Collections;
using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionConf
    {

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("strategy")]
        public string? Strategy { get; set; }

        [JsonPropertyName("provisioning_ticket_url")]
        public string? ProvisioningTicketUrl { get; set; }

        [JsonPropertyName("options")]
        public ConnectionOptions? Options { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("metadata")]
        public IDictionary? Metadata { get; set; }

        [JsonPropertyName("realms")]
        public string[]? Realms { get; set; }

        [JsonPropertyName("enabled_clients")]
        public string[]? EnabledClients { get; set; }

        [JsonPropertyName("show_as_button")]
        public bool? ShowAsButton { get; set; }

        [JsonPropertyName("is_domain_connection")]
        public bool? IsDomainConnection { get; set; }

    }

}
