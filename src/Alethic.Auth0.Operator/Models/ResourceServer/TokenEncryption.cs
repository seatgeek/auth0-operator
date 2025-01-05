using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.ResourceServer
{

    public class TokenEncryption
    {

        [JsonPropertyName("format")]
        public TokenFormat? Format { get; set; }

        [JsonPropertyName("encryption_key")]
        public TokenEncryptionKey? EncryptionKey { get; set; }

    }

}
