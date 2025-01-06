using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Client
{

    public class RefreshToken
    {

        [JsonPropertyName("rotation_type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RefreshTokenRotationType? RotationType { get; set; }

        [JsonPropertyName("expiration_type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RefreshTokenExpirationType? ExpirationType { get; set; }

        [JsonPropertyName("leeway")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Leeway { get; set; }

        [JsonPropertyName("token_lifetime")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? TokenLifetime { get; set; }

        [JsonPropertyName("infinite_token_lifetime")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? InfiniteTokenLifetime { get; set; }

        [JsonPropertyName("idle_token_lifetime")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? IdleTokenLifetime { get; set; }

        [JsonPropertyName("infinite_idle_token_lifetime")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? InfiniteIdleTokenLifetime { get; set; }
    }

}
