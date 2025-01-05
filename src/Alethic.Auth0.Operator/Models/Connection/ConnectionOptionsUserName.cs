using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsUserName
    {

        [JsonPropertyName("min")]
        public int? Min { get; set; }

        [JsonPropertyName("max")]
        public int? Max { get; set; }

    }

}