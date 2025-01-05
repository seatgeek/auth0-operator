using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Client
{

    public class RefreshToken
    {

        [JsonPropertyName("rotation_type")]
        public RefreshTokenRotationType? RotationType { get; set; }

        [JsonPropertyName("expiration_type")]
        public RefreshTokenExpirationType? ExpirationType { get; set; }

        [JsonPropertyName("leeway")]
        public int? Leeway { get; set; }

        [JsonPropertyName("token_lifetime")]
        public int? TokenLifetime { get; set; }

        [JsonPropertyName("infinite_token_lifetime")]
        public bool? InfiniteTokenLifetime { get; set; }

        [JsonPropertyName("idle_token_lifetime")]
        public int? IdleTokenLifetime { get; set; }

        [JsonPropertyName("infinite_idle_token_lifetime")]
        public bool? InfiniteIdleTokenLifetime { get; set; }
    }

}
