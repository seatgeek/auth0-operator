using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.ResourceServer
{

    public class TokenEncryptionKey
    {

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("alg")]
        public string? Algorithm { get; set; }

        [JsonPropertyName("kid")]
        public string? Kid { get; set; }

        [JsonPropertyName("pem")]
        public string? Pem { get; set; }

    }

}
