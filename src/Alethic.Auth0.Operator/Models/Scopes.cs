using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models
{
    public class Scopes
    {

        [JsonPropertyName("users")]
        public ScopeEntry? Users { get; set; }

        [JsonPropertyName("users_app_metadata")]
        public ScopeEntry? UsersAppMetadata { get; set; }

        [JsonPropertyName("clients")]
        public ScopeEntry? Clients { get; set; }

        [JsonPropertyName("client_keys")]
        public ScopeEntry? ClientKeys { get; set; }

        [JsonPropertyName("tokens")]
        public ScopeEntry? Tokens { get; set; }

        [JsonPropertyName("stats")]
        public ScopeEntry? Stats { get; set; }

    }

}
