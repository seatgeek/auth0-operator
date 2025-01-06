using System.Text.Json.Serialization;

using Auth0.ManagementApi.Models;

namespace Alethic.Auth0.Operator.Models.ResourceServer
{

    public class ProofOfPossession
    {

        [JsonPropertyName("required")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Required { get; set; }

        [JsonPropertyName("mechanism")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Mechanism? Mechanism { get; set; }

    }

}
