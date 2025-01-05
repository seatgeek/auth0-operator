using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsPasswordDictionary
    {

        [JsonPropertyName("enable")]
        public bool? Enable { get; set; }

        [JsonPropertyName("dictionary")]
        public string[]? Dictionary { get; set; }

    }

}