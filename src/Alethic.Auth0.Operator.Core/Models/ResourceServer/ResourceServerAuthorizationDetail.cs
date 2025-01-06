using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.ResourceServer
{

    public class ResourceServerAuthorizationDetail
    {

        [JsonPropertyName("type")]
        public string? Type { get; set; }

    }

}
