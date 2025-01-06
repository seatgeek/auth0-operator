using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.Client
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RefreshTokenRotationType
    {

        [JsonStringEnumMemberName("rotating")]
        Rotating,

        [JsonStringEnumMemberName("non-rotating")]
        NonRotating

    }

}
