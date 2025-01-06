using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.Connection
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ConnectionOptionsPasswordPolicy
    {

        [JsonStringEnumMemberName("none")]
        None,

        [JsonStringEnumMemberName("low")]
        Low,

        [JsonStringEnumMemberName("fair")]
        Fair,

        [JsonStringEnumMemberName("good")]
        Good,

        [JsonStringEnumMemberName("excellent")]
        Excellent

    }

}