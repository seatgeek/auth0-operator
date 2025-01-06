using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models
{

    public class BackchannelLogoutInitiators
    {

        [JsonPropertyName("mode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LogoutInitiatorModes? Mode { get; set; }

        [JsonPropertyName("selected_initiators")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LogoutInitiators[]? SelectedInitiators { get; set; }

    }

}
