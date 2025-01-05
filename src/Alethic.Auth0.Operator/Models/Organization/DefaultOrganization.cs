using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Client
{

    public class DefaultOrganization
    {

        [JsonPropertyName("organization_id")]
        public string? OrganizationId { get; set; }

        [JsonPropertyName("flows")]
        public Flows[]? Flows { get; set; }

    }

}
