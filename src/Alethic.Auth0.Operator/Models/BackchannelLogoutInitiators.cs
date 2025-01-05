using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models
{

    public class BackchannelLogoutInitiators
    {

        [JsonPropertyName("mode")]
        public LogoutInitiatorModes? Mode { get; set; }

        [JsonPropertyName("selected_initiators")]
        public LogoutInitiators[]? SelectedInitiators { get; set; }

    }

}
