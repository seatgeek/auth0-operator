using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.ResourceServer
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TokenDialect
    {

        [JsonStringEnumMemberName("access_token")]
        AccessToken,

        [JsonStringEnumMemberName("access_token_authz")]
        AccessTokenAuthZ,

        [JsonStringEnumMemberName("rfc9068_profile")]
        Rfc9068Profile,

        [JsonStringEnumMemberName("rfc9068_profile_authz")]
        Rfc9068ProfileAuthz

    }

}
