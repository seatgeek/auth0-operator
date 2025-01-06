using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Client
{

    public class ClientResourceServerAssociation
    {

        [JsonPropertyName("identifier")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Identifier { get; set; }

        [JsonPropertyName("scopes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? Scopes { get; set; }

    }

}
