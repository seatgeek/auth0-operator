using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models
{
    public class Scopes
    {

        [JsonPropertyName("users")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScopeEntry? Users { get; set; }

        [JsonPropertyName("users_app_metadata")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScopeEntry? UsersAppMetadata { get; set; }

        [JsonPropertyName("clients")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScopeEntry? Clients { get; set; }

        [JsonPropertyName("client_keys")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScopeEntry? ClientKeys { get; set; }

        [JsonPropertyName("tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScopeEntry? Tokens { get; set; }

        [JsonPropertyName("stats")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScopeEntry? Stats { get; set; }

    }

}
