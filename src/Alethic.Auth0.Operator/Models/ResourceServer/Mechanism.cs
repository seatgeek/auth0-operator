using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.ResourceServer
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Mechanism
    {

        [JsonStringEnumMemberName("mtls")]
        Mtls,

    }

}
