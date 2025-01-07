using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.ClientGrant
{

    public partial class ClientGrantConf
    {

        [JsonPropertyName("clientRef")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public V1ClientRef? ClientRef { get; set; }

        [JsonPropertyName("audience")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public V1ResourceServerRef? Audience { get; set; }

        [JsonPropertyName("organization_usage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OrganizationUsage? OrganizationUsage { get; set; }

        [JsonPropertyName("allow_any_organization")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? AllowAnyOrganization { get; set; }

        [JsonPropertyName("resourceServerRef")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? Scopes { get; set; }

    }

}
