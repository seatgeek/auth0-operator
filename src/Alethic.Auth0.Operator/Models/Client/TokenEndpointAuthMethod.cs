using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Client
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TokenEndpointAuthMethod
    {

        [JsonStringEnumMemberName("none")]
        None,

        [JsonStringEnumMemberName("client_secret_post")]
        ClientSecretPost,

        [JsonStringEnumMemberName("client_secret_basic")]
        ClientSecretBasic

    }

}
