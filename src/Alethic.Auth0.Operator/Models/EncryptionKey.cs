using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models
{

    public class EncryptionKey
    {

        [JsonPropertyName("cert")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Certificate { get; set; }

        [JsonPropertyName("pub")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PublicKey { get; set; }

        [JsonPropertyName("subject")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Subject { get; set; }

    }

}
