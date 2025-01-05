using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Client
{

    public class ClientResourceServerAssociation
    {

        [JsonPropertyName("identifier")]
        public string? Identifier { get; set; }

        [JsonPropertyName("scopes")]
        public string[]? Scopes { get; set; }

    }

}
