using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models
{

    public class OidcLogoutConfig
    {

        [JsonPropertyName("backchannel_logout_urls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? BackchannelLogoutUrls { get; set; }

        [JsonPropertyName("backchannel_logout_initiators")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public BackchannelLogoutInitiators? BackchannelLogoutInitiators { get; set; }

    }

}
