using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models
{

    public class V1ResourceServerRef
    {

        [JsonPropertyName("namespace")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Namespace { get; set; }

        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }

        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Id { get; set; }

        [JsonPropertyName("identifier")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Identifier { get; set; }

        /// <inheritdoc />
        public override string ToString()
        {
            if (Id is not null)
                return Id;
            else
                return $"{Namespace}/{Name}";
        }

    }

}
