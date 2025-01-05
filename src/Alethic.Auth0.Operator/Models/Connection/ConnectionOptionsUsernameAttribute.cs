using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsUsernameAttribute
    {

        [JsonPropertyName("identifier")]
        public ConnectionOptionsAttributeIdentifier? Identifier { get; set; }

        [JsonPropertyName("profile_required")]
        public bool? ProfileRequired { get; set; }

        [JsonPropertyName("signup")]
        public ConnectionOptionsUsernameSignup? Signup { get; set; }

        [JsonPropertyName("validation")]
        public ConnectionOptionsAttributeValidation? Validation { get; set; }

    }

}