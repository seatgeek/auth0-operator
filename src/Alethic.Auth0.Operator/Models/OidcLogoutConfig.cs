using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models
{

    public class OidcLogoutConfig
    {

        [JsonPropertyName("backchannel_logout_urls")]
        public string[]? BackchannelLogoutUrls { get; set; }

        [JsonPropertyName("backchannel_logout_initiators")]
        public BackchannelLogoutInitiators? BackchannelLogoutInitiators { get; set; }

    }

}
