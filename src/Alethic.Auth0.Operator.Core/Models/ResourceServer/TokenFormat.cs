using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.ResourceServer
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TokenFormat
    {

        [JsonStringEnumMemberName("compact-nested-jwe")]
        CompactNestedJwe,

    }

}
