using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.Connection
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ConnectionOptionsAttributeStatus
    {

        [JsonStringEnumMemberName("required")]
        Required,

        [JsonStringEnumMemberName("optional")]
        Optional,

        [JsonStringEnumMemberName("inactive")]
        Inactive

    }

}