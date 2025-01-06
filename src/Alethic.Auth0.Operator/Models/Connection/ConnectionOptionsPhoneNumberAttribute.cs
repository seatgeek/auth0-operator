using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsPhoneNumberAttribute
    {

        [JsonPropertyName("signup")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ConnectionOptionsPhoneNumberSignup? Signup { get; set; }


    }

}
