using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsAttributeAllowedTypes
    {

        [JsonPropertyName("email")]
        public bool? Email { get; set; }

        [JsonPropertyName("phone_number")]
        public bool? PhoneNumber { get; set; }

    }

}
