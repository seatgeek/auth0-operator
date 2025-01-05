using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsEmailAttribute
    {

        [JsonPropertyName("identifier")]
        public ConnectionOptionsAttributeIdentifier? Identifier { get; set; }

        [JsonPropertyName("profile_required")]
        public bool? ProfileRequired { get; set; }

        [JsonPropertyName("signup")]
        public ConnectionOptionsEmailSignup? Signup { get; set; }

    }

}
