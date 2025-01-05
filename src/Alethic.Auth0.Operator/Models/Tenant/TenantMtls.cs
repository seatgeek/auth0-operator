using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Tenant
{

    public class TenantMtls
    {

        [JsonPropertyName("enable_endpoint_aliases")]
        public bool? EnableEndpointAliases { get; set; }

    }

}
