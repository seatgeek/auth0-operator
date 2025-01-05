using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SigningAlgorithm
    {

        [JsonStringEnumMemberName("hs256")]
        HS256,

        [JsonStringEnumMemberName("rs256")]
        RS256,

        [JsonStringEnumMemberName("ps256")]
        PS256

    }

}
