using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Client
{

    public class DefaultOrganization
    {

        [JsonPropertyName("organization_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OrganizationId { get; set; }

        [JsonPropertyName("flows")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Flows[]? Flows { get; set; }

    }

}
