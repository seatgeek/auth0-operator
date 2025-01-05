using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.ResourceServer
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TokenDialect
    {

        [JsonPropertyName("access_token")]
        AccessToken,

        [JsonPropertyName("access_token_authz")]
        AccessTokenAuthZ,

        [JsonPropertyName("rfc9068_profile")]
        Rfc9068Profile,

        [JsonPropertyName("rfc9068_profile_authz")]
        Rfc9068ProfileAuthz

    }

}
