using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsPasswordComplexityOptions
    {

        [JsonPropertyName("min_length")]
        public int? MinLength { get; set; }

    }

}