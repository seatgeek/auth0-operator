using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class GatewayAuthentication
    {

        [JsonPropertyName("method")]
        public string? Method { get; set; }

        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("audience")]
        public string? Audience { get; set; }

        [JsonPropertyName("secret")]
        public string? Secret { get; set; }

        [JsonPropertyName("secret_base64_encoded")]
        public bool? SecretBase64Encoded { get; set; }

    }

}
