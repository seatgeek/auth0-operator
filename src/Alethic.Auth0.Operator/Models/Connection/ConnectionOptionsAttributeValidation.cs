using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsAttributeValidation
    {

        [JsonPropertyName("min_length")]
        public int? MinLength { get; set; }

        [JsonPropertyName("max_length")]
        public int? MaxLength { get; set; }

        [JsonPropertyName("allowed_types")]
        public ConnectionOptionsAttributeAllowedTypes? AllowedTypes { get; set; }

    }

}