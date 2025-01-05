using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Tenant
{

    public class SessionCookie
    {


        [JsonPropertyName("mode")]
        public string? Mode { get; set; }

    }

}
