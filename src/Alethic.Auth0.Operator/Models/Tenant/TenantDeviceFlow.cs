using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Entities
{

    public partial class TenantDeviceFlow
    {

        [JsonPropertyName("charset")]
        public TenantCharset? Charset { get; set; }

        [JsonPropertyName("mask")]
        public string? Mask { get; set; }

    }

}
