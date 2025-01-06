using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.ResourceServer
{

    public class TokenEncryption
    {

        [JsonPropertyName("format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TokenFormat? Format { get; set; }

        [JsonPropertyName("encryption_key")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TokenEncryptionKey? EncryptionKey { get; set; }

    }

}
