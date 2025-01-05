using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsAttributes
    {

        [JsonPropertyName("email")]
        public ConnectionOptionsEmailAttribute? Email { get; set; }

        [JsonPropertyName("phone_number")]
        public ConnectionOptionsPhoneNumberAttribute? PhoneNumber { get; set; }

        [JsonPropertyName("username")]
        public ConnectionOptionsUsernameAttribute? Username { get; set; }

    }

}