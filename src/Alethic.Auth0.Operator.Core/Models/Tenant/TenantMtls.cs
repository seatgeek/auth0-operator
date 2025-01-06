using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.Tenant
{

    public class TenantMtls
    {

        [JsonPropertyName("enable_endpoint_aliases")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? EnableEndpointAliases { get; set; }

    }

}
