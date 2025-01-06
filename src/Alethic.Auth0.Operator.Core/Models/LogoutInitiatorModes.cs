using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LogoutInitiatorModes
    {

        [JsonStringEnumMemberName("all")]
        All,

        [JsonStringEnumMemberName("custom")]
        Custom

    }

}
