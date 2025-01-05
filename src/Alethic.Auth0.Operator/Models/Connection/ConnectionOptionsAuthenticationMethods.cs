using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsAuthenticationMethods
    {

        [JsonPropertyName("password")]
        public ConnectionOptionsPasswordAuthenticationMethod? Password { get; set; }

        [JsonPropertyName("passkey")]
        public ConnectionOptionsPasskeyAuthenticationMethod? Passkey { get; set; }

    }

}