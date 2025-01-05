using System.Text.Json.Serialization;

using KubeOps.Abstractions.Entities.Attributes;

namespace Alethic.Auth0.Operator.Entities
{

    public class V1TenantRef
    {

        [JsonPropertyName("namespace")]
        public string? Namespace { get; set; }

        [JsonPropertyName("name")]
        [Required]
        public string? Name { get; set; }

    }

}
