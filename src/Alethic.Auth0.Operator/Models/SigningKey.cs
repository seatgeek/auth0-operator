using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models
{

    public class SigningKey
    {

        [JsonPropertyName("cert")]
        public string? Cert { get; set; }

        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("pkcs7")]
        public string? Pkcs7 { get; set; }

    }

}
