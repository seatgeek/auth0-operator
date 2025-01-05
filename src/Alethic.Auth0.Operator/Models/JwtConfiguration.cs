using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models
{

    public class JwtConfiguration
    {

        [JsonPropertyName("secret_encoded")]
        public bool? IsSecretEncoded { get; set; }

        [JsonPropertyName("lifetime_in_seconds")]
        public int? LifetimeInSeconds { get; set; }

        [JsonPropertyName("scopes")]
        public Scopes? Scopes { get; set; }

        [JsonPropertyName("alg")]
        public string? SigningAlgorithm { get; set; }

    }

}
