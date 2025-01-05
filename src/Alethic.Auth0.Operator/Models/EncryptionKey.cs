using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models
{

    public class EncryptionKey
    {

        [JsonPropertyName("cert")]
        public string? Certificate { get; set; }

        [JsonPropertyName("pub")]
        public string? PublicKey { get; set; }

        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

    }

}
