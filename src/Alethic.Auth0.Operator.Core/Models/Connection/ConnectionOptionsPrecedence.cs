using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.Connection
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ConnectionOptionsPrecedence
    {

        [JsonStringEnumMemberName("email")]
        Email,

        [JsonStringEnumMemberName("phone_number")]
        PhoneNumber,

        [JsonStringEnumMemberName("username")]
        UserName

    }

}