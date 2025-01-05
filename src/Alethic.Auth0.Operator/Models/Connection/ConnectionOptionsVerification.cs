using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsVerification
    {

        [JsonPropertyName("active")]
        public bool? Active { get; set; }

    }

}