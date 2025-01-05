using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsPhoneNumberAttribute
    {

        [JsonPropertyName("signup")]
        public ConnectionOptionsPhoneNumberSignup? Signup { get; set; }


    }

}
