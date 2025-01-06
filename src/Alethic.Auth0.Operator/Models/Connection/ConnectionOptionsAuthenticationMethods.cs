using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsAuthenticationMethods
    {

        [JsonPropertyName("password")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ConnectionOptionsPasswordAuthenticationMethod? Password { get; set; }

        [JsonPropertyName("passkey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ConnectionOptionsPasskeyAuthenticationMethod? Passkey { get; set; }

    }

}