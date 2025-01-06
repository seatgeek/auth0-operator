using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.ResourceServer
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Mechanism
    {

        [JsonStringEnumMemberName("mtls")]
        Mtls,

    }

}
