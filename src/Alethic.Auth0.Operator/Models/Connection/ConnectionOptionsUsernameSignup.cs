using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsUsernameSignup
    {

        [JsonPropertyName("status")]
        public ConnectionOptionsAttributeStatus? Status { get; set; }

    }

}