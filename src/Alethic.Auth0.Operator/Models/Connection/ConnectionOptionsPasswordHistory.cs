using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsPasswordHistory
    {

        [JsonPropertyName("enable")]
        public bool? Enable { get; set; }

        [JsonPropertyName("size")]
        public int? Size { get; set; }

    }

}