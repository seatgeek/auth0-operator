using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsPasswordNoPersonalInfo
    {

        [JsonPropertyName("enable")]
        public bool? Enable { get; set; }

    }

}
